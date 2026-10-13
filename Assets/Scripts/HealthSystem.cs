using UnityEngine;
using Unity.Netcode;
using TMPro;

public class HealthSystem : NetworkBehaviour
{
    [Header("HP設定")]
    public int maxHealth = 100;
    public int currentHealth; // 表示用(オンラインでは全員の画面で同じ値になる)

    [Header("吹っ飛び")]
    [Tooltip("HP が減るほど吹っ飛びやすくする(1 + 失ったHPの割合 × この値)")]
    public float knockbackGrowth = 1f;

    [Header("UI設定")]
    public GameObject hpText;
    [Tooltip("オンライン時、hpTextが空ならこの名前のオブジェクトをシーンから探します")]
    [SerializeField] private string hpTextObjectName = "HPText";

    [Header("リスポーン位置(ソロ用)")]
    [SerializeField] private Vector3 soloRespawnPosition = Vector3.zero;

    private Rigidbody2D rb;
    private SpriteRenderer[] renderers;
    private float invincibleUntil = -1f;
    private bool wasInvincible;
    private bool isDead;

    // オンライン用のHP:書き換えられるのはHostだけ、全員に自動同期される
    private readonly NetworkVariable<int> netHealth = new NetworkVariable<int>(
        3,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool IsOnline => IsSpawned;

    public bool IsInvincible => Time.time < invincibleUntil;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
    }

    private void Start()
    {
        // ソロ時だけ初期化(オンライン時はOnNetworkSpawnで初期化済み)
        if (!IsOnline) ResetPlayer();
    }

    public override void OnNetworkSpawn()
    {
        netHealth.OnValueChanged += OnNetHealthChanged;
        if (IsServer) netHealth.Value = maxHealth;
        currentHealth = netHealth.Value;
        UpdateHPUI();
    }

    public override void OnNetworkDespawn()
    {
        netHealth.OnValueChanged -= OnNetHealthChanged;
    }

    private void Update()
    {
        // オンライン時:自分のHP表示テキストをシーンから探す(ゲームシーンに入ると見つかる)
        if (IsOnline && IsOwner && hpText == null)
        {
            hpText = GameObject.Find(hpTextObjectName);
            if (hpText != null) UpdateHPUI();
        }

        // 無敵中は点滅させる
        if (!isDead)
        {
            if (IsInvincible)
            {
                bool visible = Mathf.FloorToInt(Time.time * 12f) % 2 == 0;
                SetRenderersVisible(visible);
                wasInvincible = true;
            }
            else if (wasInvincible)
            {
                SetRenderersVisible(true);
                wasInvincible = false;
            }
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            // CPU 対戦中は MatchManager が R キーを使うので、ここでは何もしない
            if (!IsOnline)
            {
                if (MatchManager.Instance == null) ResetPlayer();
            }
            else if (IsOwner) RequestResetRpc();
        }
    }

    // 弾などから呼ばれる(呼び出し方は今までと同じ)
    public void TakeDamage(int damage, Vector2 knockback)
    {
        if (!IsOnline)
        {
            if (currentHealth <= 0 || IsInvincible) return;

            currentHealth = Mathf.Max(0, currentHealth - damage);
            UpdateHPUI();

            // HP が減っているほど強く吹っ飛ぶ
            float lost = 1f - (float)currentHealth / maxHealth;
            ApplyKnockback(knockback * (1f + lost * knockbackGrowth));

            // CPU 対戦では KO の処理は MatchManager が行う
            if (currentHealth <= 0 && MatchManager.Instance == null) SetDeadVisual(true);
            return;
        }

        // オンライン時はHostだけがダメージを確定させる
        if (!IsServer || netHealth.Value <= 0) return;

        netHealth.Value = Mathf.Max(0, netHealth.Value - damage);
        KnockbackRpc(knockback);
    }

    // ダメージなしで吹き飛ばすだけ(グレネードの爆風で自分が飛ぶとき)
    public void Push(Vector2 force)
    {
        if (!IsOnline) ApplyKnockback(force);
    }

    // ===== CPU 対戦(MatchManager)から呼ばれる =====

    // KO されたとき:姿を消して動かなくする
    public void KnockOut()
    {
        currentHealth = 0;
        UpdateHPUI();
        SetDeadVisual(true);
    }

    // リスポーン:HP を全回復して指定位置に出し、しばらく無敵にする
    public void Respawn(Vector3 position, float invincibleSeconds)
    {
        currentHealth = maxHealth;
        UpdateHPUI();
        transform.position = position;
        SetDeadVisual(false);
        invincibleUntil = Time.time + invincibleSeconds;
    }

    // ===== ここから下は今までと同じ =====

    // ノックバックは位置の権限を持つ本人(Owner)のPCで実行する
    [Rpc(SendTo.Owner)]
    private void KnockbackRpc(Vector2 knockback)
    {
        ApplyKnockback(knockback);
    }

    private void ApplyKnockback(Vector2 knockback)
    {
        if (rb == null) return;
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(knockback, ForceMode2D.Impulse);
    }

    // HPが変わったら全員の画面で呼ばれる
    private void OnNetHealthChanged(int previous, int current)
    {
        currentHealth = current;
        UpdateHPUI();

        if (previous > 0 && current <= 0) SetDeadVisual(true);       // 死亡
        else if (previous <= 0 && current > 0) SetDeadVisual(false); // 復活
    }

    private void SetDeadVisual(bool dead)
    {
        isDead = dead;
        SetRenderersVisible(!dead);
        if (rb != null)
        {
            rb.simulated = !dead;
            if (!dead) rb.linearVelocity = Vector2.zero;
        }
    }

    private void SetRenderersVisible(bool visible)
    {
        if (renderers == null) return;
        foreach (var r in renderers)
            if (r != null) r.enabled = visible;
    }

    // オンライン時のリセット:Hostに依頼 → HP回復 → 本人がスポーン地点へ移動
    [Rpc(SendTo.Server)]
    private void RequestResetRpc()
    {
        netHealth.Value = maxHealth;
        RespawnRpc();
    }

    [Rpc(SendTo.Owner)]
    private void RespawnRpc()
    {
        var spawn = GetComponent<NetworkPlayerSpawn>();
        if (spawn != null) spawn.TeleportToSpawn();
    }

    // ソロ時のリセット(元の処理と同じ)
    private void ResetPlayer()
    {
        currentHealth = maxHealth;
        UpdateHPUI();
        transform.position = soloRespawnPosition;
        SetDeadVisual(false);
    }

    private void UpdateHPUI()
    {
        if (hpText == null) return;
        TextMeshProUGUI tmp = hpText.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.text = "HP: " + currentHealth;
    }
}