using UnityEngine;

// CPU(AI)の頭脳。PlayerController と WeaponSystem に入力を渡して動かす
// CPU 用のロボット(Player を複製したもの)に付ける
[RequireComponent(typeof(PlayerController))]
public class CpuController : MonoBehaviour
{
    [Header("相手")]
    [Tooltip("空なら「Player」という名前のオブジェクトを探す")]
    public Transform target;

    [Header("ステージの範囲(メインの床の左右の端)")]
    public float stageLeft = -10f;
    public float stageRight = 10f;
    [Tooltip("端からこの距離より外には自分から近づかない")]
    public float edgeMargin = 1.5f;

    [Header("動き")]
    [Tooltip("相手と保ちたい横の距離")]
    public float preferredDistance = 5f;
    [Tooltip("この幅の中なら距離はちょうどいいとみなす")]
    public float distanceTolerance = 1.5f;
    [Tooltip("考え直す間隔(秒)。小さいほど反応が速い")]
    public float thinkInterval = 0.25f;
    [Tooltip("考えるたびに、ランダムでジャンプする確率")]
    [Range(0f, 1f)] public float randomJumpChance = 0.1f;

    [Header("射撃")]
    public float fireIntervalMin = 0.5f;
    public float fireIntervalMax = 1.2f;
    [Tooltip("この距離より遠いと撃たない")]
    public float fireRange = 14f;
    [Tooltip("狙いのブレ。大きいほど外れやすい")]
    public float aimError = 0.6f;
    [Tooltip("武器を持ち替える間隔(秒)")]
    public float weaponChangeInterval = 6f;

    private PlayerController pc;
    private WeaponSystem weapon;
    private HealthSystem health;
    private HealthSystem targetHealth;
    private Rigidbody2D rb;

    private float thinkTimer;
    private float fireTimer;
    private float weaponTimer;
    private float move;
    private float strafeDir = 1f;
    private float strafeTimer;
    private Vector2 aimOffset;

    public enum Difficulty { Easy, Normal, Hard }

    // 強さに合わせて、反応・射撃・狙い・動きの数字をまとめて変える
    public void ApplyDifficulty(Difficulty d)
    {
        switch (d)
        {
            case Difficulty.Easy:
                thinkInterval = 0.45f;
                fireIntervalMin = 1.0f;
                fireIntervalMax = 1.8f;
                aimError = 1.4f;
                randomJumpChance = 0.05f;
                preferredDistance = 6f;
                weaponChangeInterval = 8f;
                break;

            case Difficulty.Normal:
                thinkInterval = 0.25f;
                fireIntervalMin = 0.5f;
                fireIntervalMax = 1.2f;
                aimError = 0.6f;
                randomJumpChance = 0.1f;
                preferredDistance = 5f;
                weaponChangeInterval = 6f;
                break;

            case Difficulty.Hard:
                thinkInterval = 0.12f;
                fireIntervalMin = 0.3f;
                fireIntervalMax = 0.7f;
                aimError = 0.25f;
                randomJumpChance = 0.18f;
                preferredDistance = 4.5f;
                weaponChangeInterval = 4f;
                break;
        }
    }

    void Awake()
    {
        pc = GetComponent<PlayerController>();
        health = GetComponent<HealthSystem>();
        rb = GetComponent<Rigidbody2D>();
        pc.isCpu = true;

        // 銃が複数付いていても、全部 CPU モードにしてマウスで撃たないようにする
        WeaponSystem[] guns = GetComponentsInChildren<WeaponSystem>(true);
        foreach (WeaponSystem g in guns)
        {
            g.isCpu = true;
            if (weapon == null && g.gameObject.activeInHierarchy) weapon = g;
        }
        if (weapon == null && guns.Length > 0) weapon = guns[0];
    }

    void Start()
    {
        if (target == null)
        {
            GameObject found = GameObject.Find("Player");
            if (found != null && found != gameObject) target = found.transform;
        }
        if (target != null) targetHealth = target.GetComponent<HealthSystem>();

        fireTimer = Random.Range(fireIntervalMin, fireIntervalMax);
        weaponTimer = weaponChangeInterval;
    }

    void Update()
    {
        // 相手がいない、または自分がやられているときは何もしない
        if (target == null || (health != null && health.currentHealth <= 0))
        {
            pc.SetCpuInput(0f, false);
            return;
        }

        Vector2 me = transform.position;
        Vector2 tp = target.position;
        bool targetAlive = targetHealth == null || targetHealth.currentHealth > 0;

        // 狙いは毎フレーム合わせる(少しブレを入れる)
        if (weapon != null) weapon.CpuAimAt(tp + Vector2.up * 0.5f + aimOffset);

        // 動きは thinkInterval ごとに考え直す
        thinkTimer -= Time.deltaTime;
        if (thinkTimer <= 0f)
        {
            thinkTimer = thinkInterval;
            Think(me, tp);
        }

        // 今の武器が熱くなっていたら、冷えている武器に持ち替える
        if (weapon != null)
        {
            int cur = weapon.CurrentIndex;
            if (weapon.IsLocked(cur) || weapon.GetHeat(cur) >= 70f)
            {
                int coolest = weapon.CoolestWeapon();
                if (coolest != cur) weapon.CpuSetWeapon(coolest);
            }
        }

        // 射撃
        fireTimer -= Time.deltaTime;
        if (fireTimer <= 0f)
        {
            fireTimer = Random.Range(fireIntervalMin, fireIntervalMax);
            if (weapon != null && targetAlive && Vector2.Distance(me, tp) <= fireRange)
            {
                weapon.CpuShoot();
                aimOffset = Random.insideUnitCircle * aimError;
            }
        }

        // ときどき武器を持ち替える
        weaponTimer -= Time.deltaTime;
        if (weaponTimer <= 0f)
        {
            weaponTimer = weaponChangeInterval;
            if (weapon != null && weapon.WeaponCount > 1)
                weapon.CpuSetWeapon(Random.Range(0, weapon.WeaponCount));
        }
    }

    void Think(Vector2 me, Vector2 tp)
    {
        bool jump = false;
        float dx = tp.x - me.x;
        float dist = Mathf.Abs(dx);
        bool offStage = me.x < stageLeft || me.x > stageRight;

        if (offStage)
        {
            // 場外に出たら、ステージの中央へ戻ろうとする。落ち始めたら空中ジャンプ
            move = me.x < 0f ? 1f : -1f;
            if (rb != null && rb.linearVelocity.y < 0f) jump = true;
        }
        else
        {
            if (dist > preferredDistance + distanceTolerance)
            {
                move = Mathf.Sign(dx);          // 遠いので近づく
            }
            else if (dist < preferredDistance - distanceTolerance)
            {
                move = -Mathf.Sign(dx);         // 近すぎるので離れる
            }
            else
            {
                // ちょうどいい距離なら、左右にゆらゆら動いて的をしぼらせない
                strafeTimer -= thinkInterval;
                if (strafeTimer <= 0f)
                {
                    strafeDir = Random.value < 0.5f ? -1f : 1f;
                    strafeTimer = Random.Range(0.8f, 2f);
                }
                move = strafeDir * 0.5f;
            }

            // 端の近くでは、外側へは進まない
            if (me.x < stageLeft + edgeMargin && move < 0f) move = 1f;
            if (me.x > stageRight - edgeMargin && move > 0f) move = -1f;

            // 相手が上の足場にいたら、ジャンプで追いかける
            if (tp.y > me.y + 1.5f && pc.IsGrounded) jump = true;

            // ときどきランダムにジャンプして、弾をよける
            if (Random.value < randomJumpChance) jump = true;
        }

        pc.SetCpuInput(move, jump);
    }
}