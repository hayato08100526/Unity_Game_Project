using UnityEngine;
using TMPro;

public class HealthSystem : MonoBehaviour
{
    [Header("HP設定")]
    public int maxHealth = 3;
    public int currentHealth;

    [Header("UI設定")]
    // ★ここを TextMeshProUGUI から GameObject に変えました！
    public GameObject hpText;

    private Rigidbody2D rb;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        ResetPlayer();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetPlayer();
        }
    }

    public void TakeDamage(int damage, Vector2 knockback)
    {
        currentHealth -= damage;
        UpdateHPUI(); // 更新命令

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.AddForce(knockback, ForceMode2D.Impulse);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        UpdateHPUI();
        GetComponent<SpriteRenderer>().enabled = false;
        if (rb != null) rb.simulated = false;
    }

    void ResetPlayer()
    {
        currentHealth = maxHealth;
        UpdateHPUI(); // 更新命令
        transform.position = new Vector3(0, 0, 0);
        GetComponent<SpriteRenderer>().enabled = true;
        if (rb != null)
        {
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
        }
    }

    // ★UI表示を更新する部分も、GameObject対応版に修正
    void UpdateHPUI()
    {
        if (hpText != null)
        {
            // hpTextの中から TextMeshProUGUI コンポーネントを探して文字を書き換える
            TextMeshProUGUI tmp = hpText.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = "HP: " + currentHealth;
            }
        }
    }
}