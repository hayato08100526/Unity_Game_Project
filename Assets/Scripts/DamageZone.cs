using UnityEngine;

public class DamageZone : MonoBehaviour
{
    public int damageAmount = 1;
    public Vector2 knockbackForce = new Vector2(20f, 10f); // わかりやすく少し強めに設定

    // 物理的に「ガツン」とぶつかった時に呼び出される
    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 【デバッグ用】誰にぶつかっても、とにかくConsoleに名前を出す！
        Debug.Log("【物理反応あり】衝突した相手: " + collision.gameObject.name);

        // ぶつかった相手が HealthSystem（体力）を持っていたら
        HealthSystem health = collision.gameObject.GetComponent<HealthSystem>();

        if (health != null)
        {
            // 敵から見てプレイヤーがどっちにいるか計算（吹き飛ばす方向）
            Vector2 direction = (collision.transform.position - transform.position).normalized;

            // 斜め上に吹き飛ばすように調整
            Vector2 finalKnockback = new Vector2(direction.x * knockbackForce.x, knockbackForce.y);

            // ダメージを与える！
            health.TakeDamage(damageAmount, finalKnockback);
        }
    }

    // 【念のため】もしTrigger設定になっていた場合もログを出す
    private void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log("【トリガー反応あり】すり抜けました。相手の名前: " + other.gameObject.name);
    }
}