using System.Collections.Generic;
using UnityEngine;

// 行ったり来たりする足場。上に乗っているロボットも一緒に運ぶ
// MatchManager が SHIFT ステージのときに自動で付ける(手で付けてもよい)
[RequireComponent(typeof(Collider2D))]
public class MovingPlatform : MonoBehaviour
{
    [Tooltip("動く幅(スタート位置から、この分だけ行って戻る)。x = 横、y = 縦")]
    public Vector2 offset = new Vector2(2f, 0f);
    [Tooltip("1往復にかかる時間(秒)")]
    public float period = 5f;
    [Tooltip("動き始めのずれ(0〜1)。0.5 にすると逆向きから動く")]
    [Range(0f, 1f)] public float phase = 0f;

    private Rigidbody2D rb;
    private Collider2D col;
    private Vector2 start;
    private float time;
    private readonly HashSet<Rigidbody2D> riders = new HashSet<Rigidbody2D>();

    void Awake()
    {
        col = GetComponent<Collider2D>();
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    void Start()
    {
        start = rb.position;
    }

    void FixedUpdate()
    {
        // 試合が始まるまでは止まっておく
        if (MatchManager.Instance != null && !MatchManager.Instance.IsFighting) return;

        time += Time.fixedDeltaTime;
        float s = Mathf.Sin((time / Mathf.Max(0.1f, period) + phase) * Mathf.PI * 2f);
        Vector2 target = start + offset * s;
        Vector2 delta = target - rb.position;
        rb.MovePosition(target);

        // 乗っているロボットも同じだけ動かす
        foreach (Rigidbody2D r in riders)
            if (r != null) r.position += delta;
        riders.Clear();
    }

    // 上に乗っているものを覚える(足元が足場の上の面より上にあるもの)
    void OnCollisionStay2D(Collision2D c)
    {
        Rigidbody2D other = c.rigidbody;
        if (other == null || other.bodyType != RigidbodyType2D.Dynamic) return;
        if (c.collider.bounds.min.y >= col.bounds.max.y - 0.15f) riders.Add(other);
    }
}