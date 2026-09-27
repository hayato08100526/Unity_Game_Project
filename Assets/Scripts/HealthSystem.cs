using UnityEngine;
using Unity.Netcode;
using TMPro;

public class HealthSystem : NetworkBehaviour
{
    [Header("HP設定")]
    public int maxHealth = 3;
    public int currentHealth; // 表示用(オンラインでは全員の画面で同じ値になる)

    [Header("UI設定")]
    public GameObject hpText;
    [Tooltip("オンライン時、hpTextが空ならこの名前のオブジェクトをシーンから探します")]
    [SerializeField] private string hpTextObjectName = "HPText";

    [Header("リスポーン位置(ソロ用)")]
    [SerializeField] private Vector3 soloRespawnPosition = Vector3.zero;

    private Rigidbody2D rb;
    private SpriteRenderer spriteRenderer;

    // オンライン用のHP:書き換えられるのはHostだけ、全員に自動同期される
    private readonly NetworkVariable<int> netHealth = new NetworkVariable<int>(
        3,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private bool IsOnline => IsSpawned;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
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

        if (Input.GetKeyDown(KeyCode.R))
        {
            if (!IsOnline) ResetPlayer();
            else if (IsOwner) RequestResetRpc();
        }
    }

    // 弾などから呼ばれる(呼び出し方は今までと同じ)
    public void TakeDamage(int damage, Vector2 knockback)
    {
        if (!IsOnline)
        {
            if (currentHealth <= 0) return;
            currentHealth -= damage;
            UpdateHPUI();
            ApplyKnockback(knockback);
            if (currentHealth <= 0) SetDeadVisual(true);
            return;
        }

        // オンライン時はHostだけがダメージを確定させる
        if (!IsServer || netHealth.Value <= 0) return;

        netHealth.Value = Mathf.Max(0, netHealth.Value - damage);
        KnockbackRpc(knockback);
    }

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
        if (spriteRenderer != null) spriteRenderer.enabled = !dead;
        if (rb != null)
        {
            rb.simulated = !dead;
            if (!dead) rb.linearVelocity = Vector2.zero;
        }
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