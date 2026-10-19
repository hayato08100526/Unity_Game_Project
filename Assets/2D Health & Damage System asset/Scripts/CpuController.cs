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

    [Header("撃つリズム(撃つ → 休む をくり返す)")]
    [Tooltip("試合開始・自分の復活のあと、撃ち始めるまでの時間(秒)")]
    public float startDelay = 2f;
    [Tooltip("1回の攻撃で撃つ弾の数")]
    public int shotsPerBurst = 3;
    [Tooltip("攻撃のあとに休む時間(秒)。この間は撃たずに動くだけ")]
    public float restMin = 1.5f;
    public float restMax = 2.5f;

    [Header("かしこさ")]
    [Tooltip("向かってくる弾をジャンプでよける確率(0〜1)")]
    [Range(0f, 1f)] public float dodgeChance = 0.45f;
    [Tooltip("オン:距離や位置で武器を選ぶ / オフ:ランダムに持ち替える")]
    public bool smartWeapons = true;
    [Tooltip("武器を選び直す間隔(秒)")]
    public float smartWeaponInterval = 1.2f;

    [Header("先手はプレイヤーに譲る")]
    [Tooltip("オン:試合開始のあと、プレイヤーが1発撃つまで CPU は撃たない")]
    public bool waitForPlayerFirstShot = false;
    [Tooltip("プレイヤーがずっと撃たないときは、この秒数がたったら CPU も撃ち始める")]
    public float maxWaitForPlayer = 5f;
    [Tooltip("プレイヤーが撃ってから、CPU が撃ち返すまでの時間(秒)")]
    public float reactionTime = 0.6f;

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
    private int shotsLeft;
    private bool wasDead;
    private WeaponSystem targetWeapon;

    // ステージの真ん中の穴(SPLIT ステージ用)
    private bool hasGap;
    private float gapLeft, gapRight;

    public void SetGap(bool on, float left, float right)
    {
        hasGap = on;
        gapLeft = left;
        gapRight = right;
    }
    private bool waitingForPlayer;
    private float waitStartTime;

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
                dodgeChance = 0.15f;
                smartWeapons = false;
                preferredDistance = 6f;
                weaponChangeInterval = 8f;
                startDelay = 1.5f;
                shotsPerBurst = 2;
                restMin = 2.5f;
                restMax = 3.5f;
                fireRange = 10f;
                maxWaitForPlayer = 8f;
                reactionTime = 1.0f;
                break;

            case Difficulty.Normal:
                thinkInterval = 0.25f;
                fireIntervalMin = 0.5f;
                fireIntervalMax = 1.2f;
                aimError = 0.6f;
                randomJumpChance = 0.1f;
                dodgeChance = 0.45f;
                smartWeapons = true;
                smartWeaponInterval = 1.2f;
                preferredDistance = 5f;
                weaponChangeInterval = 6f;
                startDelay = 1f;
                shotsPerBurst = 3;
                restMin = 1.5f;
                restMax = 2.5f;
                fireRange = 12f;
                maxWaitForPlayer = 5f;
                reactionTime = 0.6f;
                break;

            case Difficulty.Hard:
                thinkInterval = 0.12f;
                fireIntervalMin = 0.3f;
                fireIntervalMax = 0.7f;
                aimError = 0.25f;
                randomJumpChance = 0.18f;
                dodgeChance = 0.8f;
                smartWeapons = true;
                smartWeaponInterval = 0.6f;
                preferredDistance = 4.5f;
                weaponChangeInterval = 4f;
                startDelay = 0.5f;
                shotsPerBurst = 4;
                restMin = 0.8f;
                restMax = 1.4f;
                fireRange = 14f;
                maxWaitForPlayer = 3f;
                reactionTime = 0.3f;
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
        if (target != null)
        {
            foreach (WeaponSystem w in target.GetComponentsInChildren<WeaponSystem>(true))
            {
                if (targetWeapon == null) targetWeapon = w;
                if (w.gameObject.activeInHierarchy) { targetWeapon = w; break; }
            }
        }

        weaponTimer = weaponChangeInterval;
        ResetAttack();

        // 試合が始まったら、プレイヤーの1発目を待つ(待ち始めの時刻は、最初に動き出したときに決める)
        waitingForPlayer = waitForPlayerFirstShot;
        waitStartTime = -1f;
    }

    // 試合が始まったとき(MatchManager がこのスクリプトをオンにしたとき)
    void OnEnable()
    {
        ResetAttack();
    }

    // しばらく撃たずに様子を見る状態に戻す
    void ResetAttack()
    {
        fireTimer = startDelay + Random.Range(0f, 0.5f);
        shotsLeft = shotsPerBurst;
    }

    void Update()
    {
        // カウントダウンが終わるまで(とポーズ中)は、動かないし撃たない
        if (MatchManager.Instance != null && !MatchManager.Instance.IsFighting)
        {
            pc.SetCpuInput(0f, false);
            return;
        }

        // 相手がいない、または自分がやられているときは何もしない
        if (target == null || (health != null && health.currentHealth <= 0))
        {
            pc.SetCpuInput(0f, false);
            wasDead = true;
            return;
        }

        // 復活した直後は、すぐには撃たない
        if (wasDead)
        {
            wasDead = false;
            ResetAttack();
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
        // 先手はプレイヤーに譲る:プレイヤーが撃つ(か、待ちすぎる)まで撃たない
        if (waitingForPlayer)
        {
            if (waitStartTime < 0f) waitStartTime = Time.time;   // FIGHT の瞬間
            bool playerShot = targetWeapon != null && targetWeapon.LastShotTime >= waitStartTime;
            bool waitedTooLong = Time.time - waitStartTime >= maxWaitForPlayer;
            if (playerShot || waitedTooLong)
            {
                waitingForPlayer = false;
                fireTimer = playerShot ? reactionTime : Random.Range(0.2f, 0.6f);
                shotsLeft = shotsPerBurst;
            }
            else
            {
                return;   // 動きはするが、まだ撃たない
            }
        }

        // 射撃:shotsPerBurst 発撃ったら、restMin〜restMax 秒休む
        fireTimer -= Time.deltaTime;
        if (fireTimer <= 0f)
        {
            if (weapon != null && targetAlive && Vector2.Distance(me, tp) <= fireRange)
            {
                weapon.CpuShootAt(tp + Vector2.up * 0.5f);
                aimOffset = Random.insideUnitCircle * aimError;
                shotsLeft--;
            }

            if (shotsLeft <= 0)
            {
                shotsLeft = shotsPerBurst;
                fireTimer = Random.Range(restMin, restMax);   // 休む
            }
            else
            {
                fireTimer = Random.Range(fireIntervalMin, fireIntervalMax);
            }
        }

        // 武器を持ち替える(かしこいときは状況で選ぶ、そうでなければランダム)
        weaponTimer -= Time.deltaTime;
        if (weaponTimer <= 0f)
        {
            weaponTimer = smartWeapons ? smartWeaponInterval : weaponChangeInterval;
            if (weapon != null && weapon.WeaponCount > 1)
            {
                int pick = smartWeapons ? ChooseWeapon(me, tp) : Random.Range(0, weapon.WeaponCount);
                if (pick >= 0 && pick != weapon.CurrentIndex) weapon.CpuSetWeapon(pick);
            }
        }
    }

    // ===== 武器の使い分け =====
    // 状況に合う武器を、よい順に並べて、使えるもの(熱すぎない・ロックされていない)を選ぶ
    int ChooseWeapon(Vector2 me, Vector2 tp)
    {
        float dist = Vector2.Distance(me, tp);
        bool targetAbove = tp.y > me.y + 1.5f;
        bool targetLow = targetHealth != null && targetHealth.currentHealth <= targetHealth.maxHealth * 0.4f;
        bool targetNearEdge = tp.x < stageLeft + 2.5f || tp.x > stageRight - 2.5f;

        string[] order;
        if (targetLow && targetNearEdge && dist < 9f) order = new[] { "GRENADE", "SHOTGUN", "NORMAL" };   // 押し出してとどめ
        else if (dist < 4f) order = new[] { "SHOTGUN", "NORMAL", "BOUND" };                              // 近い
        else if (targetAbove) order = new[] { "BOUND", "ROUTE", "TRACK" };                               // 上の足場
        else if (dist > 9f) order = new[] { "TRACK", "ROUTE", "NORMAL" };                                // 遠い
        else order = new[] { "NORMAL", "BOUND", "TRACK" };                                               // ふつう

        foreach (string want in order)
        {
            int i = FindWeapon(want);
            if (i >= 0 && !weapon.IsLocked(i) && weapon.GetHeat(i) < 70f) return i;
        }
        return weapon.CoolestWeapon();
    }

    int FindWeapon(string prefix)
    {
        for (int i = 0; i < weapon.WeaponCount; i++)
        {
            string n = weapon.GetWeaponName(i);
            if (n != null && n.ToUpper().StartsWith(prefix)) return i;
        }
        return -1;
    }

    // ===== 弾よけ =====
    // 近くで、自分の方へ飛んでくる弾があるか
    static readonly Collider2D[] nearby = new Collider2D[16];

    bool IncomingBullet(Vector2 me)
    {
        ContactFilter2D f = new ContactFilter2D();
        f.useTriggers = true;
        int n = Physics2D.OverlapCircle(me + Vector2.up * 0.5f, 3.2f, f, nearby);
        for (int i = 0; i < n; i++)
        {
            Collider2D c = nearby[i];
            if (c == null) continue;
            bool isBullet = c.GetComponent<Bullet>() != null || c.GetComponent<RouteBullet>() != null || c.GetComponent<GrenadeBullet>() != null;
            if (!isBullet) continue;

            Rigidbody2D brb = c.attachedRigidbody;
            Vector2 v = brb != null ? brb.linearVelocity : Vector2.zero;
            if (v.sqrMagnitude < 4f) continue;
            Vector2 toMe = me + Vector2.up * 0.5f - (Vector2)c.transform.position;
            if (Vector2.Dot(v.normalized, toMe.normalized) > 0.6f) return true;   // こっちに向かってくる
        }
        return false;
    }

    void Think(Vector2 me, Vector2 tp)
    {
        bool jump = false;

        // 向かってくる弾をよける(強さで確率が変わる)
        if (dodgeChance > 0f && pc.IsGrounded && IncomingBullet(me) && Random.value < dodgeChance) jump = true;   // 空中ジャンプは復帰用に残す
        float dx = tp.x - me.x;
        float dist = Mathf.Abs(dx);
        bool offStage = me.x < stageLeft || me.x > stageRight;

        // 穴に落ちかけている(穴の上で、床より低い)ときは、近い方の島へ戻る
        if (hasGap && me.x > gapLeft && me.x < gapRight && me.y < 0.5f)
        {
            move = (me.x < (gapLeft + gapRight) / 2f) ? -1f : 1f;
            if (rb != null && rb.linearVelocity.y < 0f) jump = true;
            pc.SetCpuInput(move, jump);
            return;
        }

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

            // 穴のふちに来たら、ジャンプで飛び越える
            if (hasGap && pc.IsGrounded)
            {
                bool nearLeftEdge = me.x > gapLeft - 1.2f && me.x < gapLeft && move > 0f;
                bool nearRightEdge = me.x < gapRight + 1.2f && me.x > gapRight && move < 0f;
                if (nearLeftEdge || nearRightEdge)
                {
                    move = Mathf.Sign(move);   // 勢いをつけて
                    jump = true;
                }
            }

            // HP が減っている(REPAIR)か、武器が熱い(COOLANT)ときは、近くのアイテムを取りに行く
            ItemPickup want = null;
            float best = 7f;
            foreach (ItemPickup it in ItemPickup.Active)
            {
                if (it == null) continue;
                bool useful = it.kind == ItemPickup.Kind.Repair
                    ? (health != null && health.currentHealth < health.maxHealth * 0.6f)
                    : (weapon != null && weapon.MaxHeat() >= 60f);
                float d = Vector2.Distance(me, it.transform.position);
                if (useful && d < best) { best = d; want = it; }
            }
            if (want != null)
            {
                Vector2 ip = want.transform.position;
                move = Mathf.Abs(ip.x - me.x) > 0.3f ? Mathf.Sign(ip.x - me.x) : 0f;
                if (ip.y > me.y + 1.2f && pc.IsGrounded) jump = true;
                pc.SetCpuInput(move, jump);
                return;
            }

            // 相手が上の足場にいたら、ジャンプで追いかける
            if (tp.y > me.y + 1.5f && pc.IsGrounded) jump = true;

            // ときどきランダムにジャンプして、弾をよける
            if (Random.value < randomJumpChance) jump = true;
        }

        pc.SetCpuInput(move, jump);
    }
}