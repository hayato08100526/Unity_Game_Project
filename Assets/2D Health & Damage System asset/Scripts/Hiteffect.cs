using UnityEngine;

// 当たったときの火花と、KO のときの破片を出す係。シーンに置かなくても動く
// 使い方:HitEffect.Spark(位置, 頭かどうか) / HitEffect.Burst(位置, 色)
public class HitEffect : MonoBehaviour
{
    private static Sprite dot;      // 小さい四角(プログラムで作る)

    private Vector2 velocity;
    private float life, maxLife;
    private float gravity;
    private SpriteRenderer sr;
    private Color baseColor;
    private Vector3 baseScale;

    static Sprite Dot()
    {
        if (dot == null)
        {
            Texture2D tex = new Texture2D(4, 4);
            Color[] px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Point;
            tex.Apply();
            dot = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 40f); // 0.1 マスの四角
        }
        return dot;
    }

    static void Spawn(Vector2 pos, Vector2 vel, Color color, float size, float lifeTime, float grav)
    {
        GameObject go = new GameObject("HitFx");
        go.transform.position = new Vector3(pos.x, pos.y, -1f);
        go.transform.localScale = Vector3.one * size;
        var fx = go.AddComponent<HitEffect>();
        fx.sr = go.AddComponent<SpriteRenderer>();
        fx.sr.sprite = Dot();
        fx.sr.sortingOrder = 20;         // ロボットや足場より手前
        fx.sr.color = color;
        fx.baseColor = color;
        fx.baseScale = go.transform.localScale;
        fx.velocity = vel;
        fx.life = fx.maxLife = lifeTime;
        fx.gravity = grav;
    }

    // 弾が当たったときの火花
    public static void Spark(Vector2 pos, bool head)
    {
        int count = head ? 12 : 7;
        Color hot = head ? new Color32(0xFF, 0x8A, 0x3D, 0xFF) : new Color32(0xFF, 0xE2, 0xA8, 0xFF);
        for (int i = 0; i < count; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float sp = Random.Range(3f, head ? 9f : 6.5f);
            Color c = Random.value < 0.35f ? Color.white : hot;
            Spawn(pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, c, Random.Range(0.8f, 1.5f), Random.Range(0.15f, 0.3f), 6f);
        }
    }

    // KO のときの大きな破片
    public static void Burst(Vector2 pos, Color color)
    {
        for (int i = 0; i < 28; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float sp = Random.Range(4f, 13f);
            Color c = Random.value < 0.3f ? Color.white : color;
            Spawn(pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp, c, Random.Range(1.2f, 2.6f), Random.Range(0.4f, 0.8f), 9f);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        life -= dt;
        if (life <= 0f)
        {
            Destroy(gameObject);
            return;
        }
        velocity.y -= gravity * dt;
        velocity *= 1f - 2.5f * dt;   // だんだん遅く
        transform.position += (Vector3)(velocity * dt);

        float u = life / maxLife;    // 1 → 0
        transform.localScale = baseScale * (0.4f + 0.6f * u);
        sr.color = new Color(baseColor.r, baseColor.g, baseColor.b, u);
    }
}