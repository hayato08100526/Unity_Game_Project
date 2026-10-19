using UnityEngine;

// 背景を動かす係:首を振るサーチライト、ときどき横切る貨物ドローン、舞い上がるちり
// シーンに空のオブジェクトを作って付けるだけ(絵はプログラムで作る)
public class BackgroundFX : MonoBehaviour
{
    [Header("サーチライト")]
    public bool searchlights = true;
    [Range(0f, 0.3f)] public float lightAlpha = 0.08f;

    [Header("貨物ドローン")]
    public bool drones = true;
    public float droneIntervalMin = 14f;
    public float droneIntervalMax = 24f;

    [Header("ちり")]
    public bool dust = true;
    public int dustCount = 45;

    private Transform farLayer, midLayer;

    // サーチライト
    private Transform[] beams;
    private float[] beamSpeed, beamPhase;

    // ドローン
    private Transform drone;
    private SpriteRenderer droneLight;
    private float droneTimer, droneDir;

    // ちり
    private Transform[] dusts;
    private Vector2[] dustVel;

    // ===== ステージの雰囲気(0 = 夜 / 1 = 夕焼け / 2 = 雷雨) =====
    private int mood = 0;
    private SpriteRenderer skyOverlay;   // 空にかぶせる色(カメラについてくる)
    private SpriteRenderer flash;        // 雷の光
    private Transform[] rain;
    private float lightningTimer, flashLeft;

    private bool started;
    private int pendingMood = -1;

    public void SetMood(int stage)
    {
        // まだ準備(Start)の前なら、準備が終わってから変える
        if (!started) { pendingMood = stage; return; }
        mood = stage;
        Color farTint = Color.white, midTint = Color.white;
        Color top = new Color(0, 0, 0, 0), bottom = new Color(0, 0, 0, 0);

        if (stage == 1)   // 夕焼け
        {
            top = new Color(0.35f, 0.15f, 0.5f, 0.28f);
            bottom = new Color(1f, 0.45f, 0.2f, 0.42f);
            farTint = new Color(1f, 0.82f, 0.8f);
            midTint = new Color(1f, 0.88f, 0.86f);
            MakeSun();
            if (beams != null)
                foreach (Transform b in beams)
                    b.GetComponent<SpriteRenderer>().color = new Color(Amber.r, Amber.g, Amber.b, lightAlpha);
        }
        else if (stage == 2)   // 雷雨
        {
            top = new Color(0.05f, 0.08f, 0.16f, 0.45f);
            bottom = new Color(0.1f, 0.18f, 0.28f, 0.3f);
            farTint = new Color(0.72f, 0.82f, 1f);
            midTint = new Color(0.78f, 0.88f, 1f);
            MakeRain();
            MakeFlash();
            lightningTimer = Random.Range(4f, 8f);
        }

        Tint("BG_Far", farTint);
        Tint("BG_Mid", midTint);
        Tint("BG_Near", midTint);
        if (stage != 0) MakeSkyOverlay(top, bottom);
    }

    void Tint(string objectName, Color c)
    {
        GameObject go = GameObject.Find(objectName);
        if (go == null) return;
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = c;
    }

    void MakeSkyOverlay(Color top, Color bottom)
    {
        const int H = 64;
        Texture2D tex = new Texture2D(1, H) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1);   // 0 = 下, 1 = 上
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.85f, v));
            tex.SetPixel(0, y, Color.Lerp(bottom, top, k));
        }
        tex.Apply();
        GameObject go = new GameObject("SkyOverlay");
        skyOverlay = go.AddComponent<SpriteRenderer>();
        skyOverlay.sprite = Sprite.Create(tex, new Rect(0, 0, 1, H), new Vector2(0.5f, 0.5f), 1f);
        skyOverlay.sortingOrder = -19;   // 遠景(-20)のすぐ手前
    }

    void MakeSun()
    {
        const int N = 128;
        Texture2D tex = new Texture2D(N, N) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                float disc = d < 0.55f ? 1f : 0f;
                float glow = Mathf.Clamp01(1f - d) * 0.6f;
                float a = Mathf.Max(disc, glow * glow);
                Color c = disc > 0f ? new Color(1f, 0.72f, 0.42f, 1f) : new Color(1f, 0.55f, 0.25f, a);
                // 太陽に横じまを入れる(レトロな感じ)
                if (disc > 0f && y < N / 2 && (y / 6) % 2 == 0) c = new Color(1f, 0.55f, 0.3f, 1f);
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        GameObject go = new GameObject("Sun");
        go.transform.SetParent(farLayer, true);
        go.transform.position = new Vector3(farLayer.position.x + 6f, 3.2f, 0f);
        go.transform.localScale = Vector3.one * 6f;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        sr.sortingOrder = -19;
    }

    void MakeRain()
    {
        Texture2D tex = new Texture2D(1, 8) { filterMode = FilterMode.Point };
        for (int y = 0; y < 8; y++) tex.SetPixel(0, y, new Color(1f, 1f, 1f, y / 7f));
        tex.Apply();
        Sprite drop = Sprite.Create(tex, new Rect(0, 0, 1, 8), new Vector2(0.5f, 0.5f), 20f);
        rain = new Transform[160];
        for (int i = 0; i < rain.Length; i++)
        {
            GameObject go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(Random.Range(-16f, 16f), Random.Range(-6f, 12f), 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, 12f);
            go.transform.localScale = new Vector3(1.2f, Random.Range(1.2f, 2.2f), 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = drop;
            bool front = i % 5 == 0;   // 5 本に 1 本は手前(うすく)
            sr.color = new Color(0.7f, 0.85f, 1f, front ? 0.18f : 0.35f);
            sr.sortingOrder = front ? 25 : -4;
            rain[i] = go.transform;
        }
    }

    void MakeFlash()
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        GameObject go = new GameObject("Lightning");
        flash = go.AddComponent<SpriteRenderer>();
        flash.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        flash.color = new Color(0.8f, 0.9f, 1f, 0f);
        flash.sortingOrder = -18;
    }

    // 毎フレーム:空の色と雷の光をカメラに合わせる、雨を降らせる
    void UpdateMood()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float h = cam.orthographicSize * 2f, w = h * cam.aspect;
        Vector3 cpos = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);

        if (skyOverlay != null)
        {
            skyOverlay.transform.position = cpos;
            skyOverlay.transform.localScale = new Vector3(w + 2f, (h + 2f) / 64f, 1f);
        }

        if (flash != null)
        {
            flash.transform.position = cpos;
            flash.transform.localScale = new Vector3(w + 2f, h + 2f, 1f);
            lightningTimer -= Time.deltaTime;
            if (lightningTimer <= 0f)
            {
                lightningTimer = Random.Range(6f, 12f);
                flashLeft = 0.45f;
            }
            if (flashLeft > 0f)
            {
                flashLeft -= Time.deltaTime;
                // ピカッ、ピカッと2回光る
                float a = (flashLeft > 0.33f || (flashLeft > 0.12f && flashLeft < 0.22f)) ? 0.35f : 0f;
                flash.color = new Color(0.8f, 0.9f, 1f, a);
            }
            else flash.color = new Color(0.8f, 0.9f, 1f, 0f);
        }

        if (rain != null)
        {
            Vector3 v = new Vector3(-3.5f, -17f, 0f) * Time.deltaTime;
            for (int i = 0; i < rain.Length; i++)
            {
                Vector3 p = rain[i].position + v;
                if (p.y < cpos.y - h / 2f - 1f) p = new Vector3(cpos.x + Random.Range(-w / 2f - 2f, w / 2f + 4f), cpos.y + h / 2f + 1f, 0f);
                if (p.x < cpos.x - w / 2f - 3f) p.x += w + 6f;
                rain[i].position = p;
            }
        }
    }

    static readonly Color Cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
    static readonly Color Amber = new Color32(0xFF, 0xB5, 0x47, 0xFF);

    void Start()
    {
        GameObject far = GameObject.Find("BG_Far");
        GameObject mid = GameObject.Find("BG_Mid");
        farLayer = far != null ? far.transform : transform;
        midLayer = mid != null ? mid.transform : transform;

        if (searchlights) MakeSearchlights();
        if (drones) { MakeDrone(); droneTimer = Random.Range(3f, 6f); }
        if (dust) MakeDust();

        started = true;
        if (pendingMood >= 0) SetMood(pendingMood);
    }

    void Update()
    {
        float t = Time.time;
        if (mood != 0) UpdateMood();

        // サーチライト:ゆっくり左右に首を振る
        if (beams != null)
            for (int i = 0; i < beams.Length; i++)
                beams[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * beamSpeed[i] + beamPhase[i]) * 28f);

        // ドローン:ときどき画面の端から端まで横切る
        if (drone != null)
        {
            if (drone.gameObject.activeSelf)
            {
                drone.position += Vector3.right * droneDir * 2.4f * Time.deltaTime;
                drone.position += Vector3.up * Mathf.Sin(t * 2f) * 0.003f;
                droneLight.color = (Mathf.FloorToInt(t * 3f) % 2 == 0) ? new Color(1f, 0.35f, 0.35f, 1f) : new Color(0.25f, 0.08f, 0.08f, 1f);
                if (Mathf.Abs(drone.position.x - farLayer.position.x) > 26f) drone.gameObject.SetActive(false);
            }
            else
            {
                droneTimer -= Time.deltaTime;
                if (droneTimer <= 0f)
                {
                    droneTimer = Random.Range(droneIntervalMin, droneIntervalMax);
                    droneDir = Random.value < 0.5f ? 1f : -1f;
                    drone.position = new Vector3(farLayer.position.x - 25f * droneDir, Random.Range(6.5f, 9.5f), 0f);
                    drone.localScale = new Vector3(droneDir, 1f, 1f);
                    drone.gameObject.SetActive(true);
                }
            }
        }

        // ちり:ゆっくり上へ。上まで行ったら下から出直す
        if (dusts != null)
        {
            Camera cam = Camera.main;
            Vector2 c = cam != null ? (Vector2)cam.transform.position : Vector2.zero;
            for (int i = 0; i < dusts.Length; i++)
            {
                Vector3 p = dusts[i].position + (Vector3)(dustVel[i] * Time.deltaTime);
                p.x += Mathf.Sin(t * 0.7f + i) * 0.002f;
                if (p.y > c.y + 9f) p = new Vector3(c.x + Random.Range(-15f, 15f), c.y - 9f, 0f);
                if (p.x < c.x - 16f) p.x += 32f;
                if (p.x > c.x + 16f) p.x -= 32f;
                dusts[i].position = p;
            }
        }
    }

    // ===== 作る =====

    void MakeSearchlights()
    {
        Sprite beam = BeamSprite();
        Vector2[] bases = { new Vector2(-7f, -1f), new Vector2(7.5f, -1f) };
        beams = new Transform[bases.Length];
        beamSpeed = new float[bases.Length];
        beamPhase = new float[bases.Length];
        for (int i = 0; i < bases.Length; i++)
        {
            GameObject go = new GameObject("Searchlight_" + i);
            go.transform.SetParent(midLayer, false);
            go.transform.position = new Vector3(bases[i].x + midLayer.position.x, bases[i].y, 0f);
            go.transform.localScale = new Vector3(5f, 16f, 1f) * (1f / Mathf.Max(0.01f, midLayer.lossyScale.y));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = beam;
            sr.color = new Color(Cyan.r, Cyan.g, Cyan.b, lightAlpha);
            sr.sortingOrder = -12;   // 遠景(-20)と工場(-10)のあいだ
            beams[i] = go.transform;
            beamSpeed[i] = Random.Range(0.25f, 0.4f);
            beamPhase[i] = i * 2.1f;
        }
    }

    void MakeDrone()
    {
        GameObject go = new GameObject("CargoDrone");
        go.transform.SetParent(farLayer, true);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = DroneSprite();
        sr.sortingOrder = -15;

        GameObject light = new GameObject("Light");
        light.transform.SetParent(go.transform, false);
        light.transform.localPosition = new Vector3(0.45f, -0.05f, 0f);
        droneLight = light.AddComponent<SpriteRenderer>();
        droneLight.sprite = DotSprite();
        droneLight.sortingOrder = -14;
        light.transform.localScale = Vector3.one * 1.5f;

        drone = go.transform;
        go.SetActive(false);
    }

    void MakeDust()
    {
        Sprite dot = DotSprite();
        dusts = new Transform[dustCount];
        dustVel = new Vector2[dustCount];
        for (int i = 0; i < dustCount; i++)
        {
            GameObject go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(Random.Range(-15f, 15f), Random.Range(-3f, 11f), 0f);
            go.transform.localScale = Vector3.one * Random.Range(0.4f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = dot;
            Color c = Random.value < 0.7f ? Cyan : Amber;
            sr.color = new Color(c.r, c.g, c.b, Random.Range(0.12f, 0.35f));
            sr.sortingOrder = -8;    // 工場より手前、足場やロボットより奥
            dusts[i] = go.transform;
            dustVel[i] = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.15f, 0.45f));
        }
    }

    // ===== 絵(プログラムで作る) =====

    static Sprite dotSprite, beamSprite, droneSprite;

    static Sprite DotSprite()
    {
        if (dotSprite != null) return dotSprite;
        Texture2D tex = new Texture2D(2, 2) { filterMode = FilterMode.Point };
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        dotSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 40f); // 0.05 マス
        return dotSprite;
    }

    // 下がとがって上に広がる、光の帯(根元が明るく、先と両端が消える)
    static Sprite BeamSprite()
    {
        if (beamSprite != null) return beamSprite;
        const int W = 64, H = 256;
        Texture2D tex = new Texture2D(W, H) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < H; y++)
        {
            float v = y / (float)(H - 1);          // 0 = 根元, 1 = 先
            float half = Mathf.Lerp(0.04f, 0.5f, v);
            for (int x = 0; x < W; x++)
            {
                float u = Mathf.Abs(x / (float)(W - 1) - 0.5f);
                float side = Mathf.Clamp01(1f - u / half);
                float a = side * side * (1f - v) * (1f - v * 0.3f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        beamSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), H); // 高さ 1 マス(Scale で伸ばす)
        return beamSprite;
    }

    // 横長の貨物ドローン(右向き)
    static Sprite DroneSprite()
    {
        if (droneSprite != null) return droneSprite;
        const int W = 28, H = 12;
        Texture2D tex = new Texture2D(W, H) { filterMode = FilterMode.Point };
        Color clear = new Color(0, 0, 0, 0);
        Color body = new Color32(0x1C, 0x24, 0x30, 0xFF);
        Color edge = new Color32(0x2C, 0x37, 0x46, 0xFF);
        Color box = new Color32(0x34, 0x2A, 0x16, 0xFF);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = clear;
                if (y >= 6 && y <= 8 && x >= 4 && x <= 23) c = body;            // 胴体
                if (y == 9 && x >= 4 && x <= 23) c = edge;
                if (y == 10 && ((x >= 1 && x <= 7) || (x >= 20 && x <= 26))) c = edge;  // プロペラ
                if (y >= 1 && y <= 5 && x >= 9 && x <= 18) c = box;             // つり下げた荷物
                if (y == 5 && x >= 9 && x <= 18) c = new Color32(0xFF, 0xB5, 0x47, 0xFF);
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        droneSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 28f); // 横 1 マス
        return droneSprite;
    }
}