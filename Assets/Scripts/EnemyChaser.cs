using UnityEngine;

public class EnemyChaser : MonoBehaviour
{
    public Transform target;     // 追いかける相手（Player）
    public float speed = 4f;     // 追いかけるスピード
    private Rigidbody2D rb;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        // もしInspectorでセットし忘れても、自動でPlayerを探す
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }
    }

    void FixedUpdate()
    {
        if (target == null) return;

        // プレイヤーへの方向を計算
        Vector2 direction = (target.position - transform.position).normalized;

        // プレイヤーに向かって力を加える
        // Y軸（高さ）は無視せず、ふわふわ追いかけてくる設定にします
        rb.linearVelocity = new Vector2(direction.x * speed, rb.linearVelocity.y);

        // 向きを変える（プレイヤーの方を向く）
        if (direction.x > 0) transform.localScale = new Vector3(1, 1, 1);
        else if (direction.x < 0) transform.localScale = new Vector3(-1, 1, 1);
    }
}