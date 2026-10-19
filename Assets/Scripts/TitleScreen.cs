using UnityEngine;
using UnityEngine.SceneManagement;

// タイトル画面(VS CPU / ONLINE MATCH / HOW TO PLAY / QUIT)と、動くお手本つきの操作説明画面
// MainMenu シーンに空のオブジェクトを作って付ける
public class TitleScreen : MonoBehaviour
{
    [Header("移動先のシーン名")]
    public string cpuSceneName = "CpuBattle";
    public string onlineSceneName = "OnlineLobbyScene";

    [Header("見た目")]
    [Tooltip("英字のフォント(Oxanium-Bold など)。空なら標準のフォント")]
    public Font titleFont;
    [Tooltip("日本語のフォント(Noto Sans JP など)。空なら標準のフォント(Windows の日本語フォントが使われる)")]
    public Font japaneseFont;

    [Header("タイトルの絵(空でも動く)")]
    [Tooltip("Backgrounds の BG_Far")]
    public Texture2D background;
    [Tooltip("Robots の Robot_P1 / Robot_CPU")]
    public Texture2D robotP1;
    public Texture2D robotCpu;
    [Tooltip("Robots の Robot_Gun_P1 / Robot_Gun_CPU")]
    public Texture2D gunP1;
    public Texture2D gunCpu;

    private GUIStyle titleStyle;
    private GUIStyle subStyle;
    private GUIStyle buttonStyle;
    private GUIStyle helpStyle;
    private GUIStyle chipStyle;
    private GUIStyle jpHead;
    private GUIStyle jpText;

    private readonly Color bg = new Color32(0x07, 0x09, 0x0D, 0xFF);
    private readonly Color card = new Color32(0x0D, 0x11, 0x17, 0xFF);
    private readonly Color stage = new Color32(0x10, 0x16, 0x1E, 0xFF);
    private readonly Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);
    private readonly Color plat = new Color32(0x3A, 0x44, 0x52, 0xFF);
    private readonly Color cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
    private readonly Color amber = new Color32(0xFF, 0xB5, 0x47, 0xFF);
    private readonly Color hot = new Color32(0xFF, 0x8A, 0x3D, 0xFF);
    private readonly Color red = new Color32(0xFF, 0x5A, 0x5A, 0xFF);
    private readonly Color white = new Color32(0xE6, 0xED, 0xF3, 0xFF);
    private readonly Color grey = new Color32(0x8B, 0x98, 0xA8, 0xFF);

    private bool showHowTo = false;
    private int tab = 0;
    private static readonly string[] tabNames = { "そうさ", "ぶき", "ルール" };

    // ===== 説明の中身(ゲームの数字を変えたらここも直す) =====

    // 見出し, キー, 説明
    private static readonly string[,] controls =
    {
        { "移動",         "A / D",          "左右に動く" },
        { "ジャンプ",     "SPACE",          "空中でもう一度押すと 2段ジャンプ" },
        { "壁キック",     "SPACE",          "空中で壁にふれて押すと、反対側へ跳ぶ" },
        { "ダッシュ",     "L-SHIFT",        "動きながら押すと、一瞬で前へ進む" },
        { "ねらう",       "MOUSE",          "マウスのある方へ銃が向く" },
        { "撃つ",         "CLICK / ENTER",  "押すたびに 1発。武器ごとに撃てる間隔がある" },
        { "武器を変える", "1-6 / WHEEL",    "数字キーかホイールで持ち替え。少し時間がかかる" },
        { "ポーズ",       "ESC / P",        "試合を止める。R でやり直し、T でタイトルへ" },
    };

    private static readonly string[] weaponNames = { "NORMAL", "BOUND", "TRACKING", "SHOTGUN", "GRENADE", "ROUTE" };
    private static readonly string[] weaponNotes =
    {
        "まっすぐ速く飛ぶ基本の弾。熱がたまりにくい。迷ったらこれ。",
        "床や壁で跳ね返る弾。足場の上の相手もねらえる。",
        "相手に向かって曲がっていく弾。当てやすい。",
        "5発が扇形に広がる。近いほど強い。",
        "山なりに飛んで爆発。まわりごと吹き飛ばす。",
        "押しながら線を描き、離すと線の通りに飛ぶ。描いている間は移動が遅い。",
    };
    private static readonly float[] weaponHeat = { 8f, 12f, 25f, 30f, 35f, 40f };
    private static readonly float[] weaponInterval = { 0.15f, 0.3f, 0.6f, 0.6f, 0.8f, 1.0f };

    private static readonly string[] ruleHeads = { "ストックと KO", "熱とオーバーヒート", "部位破壊" };
    private static readonly string[][] ruleLines =
    {
        new[]
        {
            "ストックは 3つ。全部なくなると負け",
            "場外に飛ばされると KO",
            "HP が減るほど、攻撃で遠くへ飛ばされる",
            "3分たったら、ストック → HP の多い方の勝ち",
        },
        new[]
        {
            "撃つたびに、その武器に熱がたまる",
            "100 になるとオーバーヒート。30秒使えない",
            "持っていない武器は 2倍の速さで冷える",
            "熱くなったら、ほかの武器に持ち替えよう",
        },
        new[]
        {
            "当たった場所でダメージが変わる",
            "頭:ダメージ 1.5倍。壊れると照準がぶれる",
            "腕:壊れると、撃つのと持ち替えが遅くなる",
            "脚:壊れると、移動が遅くジャンプが低くなる",
        },
    };

    // アニメーション用の時間(ポーズ中でも動くように unscaledTime を使う)
    private float T => Time.unscaledTime;

    void Start()
    {
        Time.timeScale = 1f;
    }

    void Update()
    {
        if (showHowTo)
        {
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) tab = (tab + 1) % tabNames.Length;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) tab = (tab + tabNames.Length - 1) % tabNames.Length;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace)) showHowTo = false;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            LoadScene(cpuSceneName);

        if (Input.GetKeyDown(KeyCode.H)) OpenHowTo();
    }

    void OpenHowTo()
    {
        showHowTo = true;
        tab = 0;
    }

    void OnGUI()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            helpStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            chipStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            jpHead = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            jpText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true };

            if (titleFont != null)
            {
                titleStyle.font = titleFont;
                subStyle.font = titleFont;
                buttonStyle.font = titleFont;
                chipStyle.font = titleFont;
            }
            if (japaneseFont != null)
            {
                jpHead.font = japaneseFont;
                jpText.font = japaneseFont;
                helpStyle.font = japaneseFont;
            }
        }

        float unit = Screen.height / 20f;
        titleStyle.fontSize = Mathf.RoundToInt(unit * 2.6f);
        subStyle.fontSize = Mathf.RoundToInt(unit * 0.55f);
        buttonStyle.fontSize = Mathf.RoundToInt(unit * 0.8f);
        helpStyle.fontSize = Mathf.RoundToInt(unit * 0.45f);
        chipStyle.fontSize = Mathf.RoundToInt(unit * 0.42f);
        jpHead.fontSize = Mathf.RoundToInt(unit * 0.62f);
        jpText.fontSize = Mathf.RoundToInt(unit * 0.46f);

        Fill(new Rect(0, 0, Screen.width, Screen.height), bg);

        if (showHowTo) DrawHowTo(unit);
        else DrawTitle(unit);
    }

    // ===== タイトル =====

    void DrawTitle(float unit)
    {
        DrawTitleScene(unit);

        titleStyle.normal.textColor = white;
        GUI.Label(new Rect(0, Screen.height * 0.1f, Screen.width, unit * 3f), "B A T T L E   D O O M", titleStyle);

        subStyle.normal.textColor = cyan;
        GUI.Label(new Rect(0, Screen.height * 0.1f + unit * 3f, Screen.width, unit), "R O B O T   V E R S U S   S H O O T E R", subStyle);

        float w = unit * 9f, h = unit * 1.5f, gap = unit * 0.45f;
        float x = (Screen.width - w) / 2f;
        float y = Screen.height * 0.38f;

        if (Button(new Rect(x, y, w, h), "VS CPU", true)) LoadScene(cpuSceneName);
        if (Button(new Rect(x, y + (h + gap), w, h), "ONLINE MATCH", false)) LoadScene(onlineSceneName);
        if (Button(new Rect(x, y + (h + gap) * 2, w, h), "HOW TO PLAY", false)) OpenHowTo();
        if (Button(new Rect(x, y + (h + gap) * 3, w, h), "QUIT", false)) Application.Quit();

        helpStyle.normal.textColor = grey;
        GUI.Label(new Rect(unit, Screen.height - unit * 1.6f, Screen.width - unit * 2f, unit),
                  "ENTER:すぐに CPU 対戦      H:あそびかた", helpStyle);
    }

    // ===== タイトルの絵:夜景の背景と、向かい合う2体のロボット =====
    void DrawTitleScene(float unit)
    {
        float W = Screen.width, H = Screen.height;

        // 背景(画面いっぱいに切り抜いて表示)+ 少し暗く
        if (background != null)
        {
            GUI.DrawTexture(new Rect(0, 0, W, H), background, ScaleMode.ScaleAndCrop);
            Fill(new Rect(0, 0, W, H), new Color(bg.r, bg.g, bg.b, 0.55f));
        }

        // 床
        float floorY = H - unit * 2.6f;
        Fill(new Rect(0, floorY, W, H - floorY), new Color32(0x0A, 0x0E, 0x14, 0xF0));
        Fill(new Rect(0, floorY, W, 2f), new Color32(0x3A, 0x44, 0x52, 0xFF));

        if (robotP1 == null || robotCpu == null) return;

        // ロボット(ふわっと上下に揺れる)
        float rh = unit * 7.5f;                           // ロボットの高さ
        float rw = rh * robotP1.width / robotP1.height;
        float k = rh / 72f;                               // 絵の 1 ドットの大きさ
        float bob = Mathf.Sin(Time.unscaledTime * 2f) * unit * 0.08f;

        Rect left = new Rect(W * 0.17f - rw / 2f, floorY - rh + bob, rw, rh);
        Rect right = new Rect(W * 0.83f - rw / 2f, floorY - rh - bob, rw, rh);
        GUI.DrawTexture(left, robotP1);
        GUI.DrawTextureWithTexCoords(right, robotCpu, new Rect(1f, 0f, -1f, 1f)); // 左右反転

        // 銃(肩から相手の方へ)
        float shY = 29f * k;                              // 肩の高さ(絵の上から)
        Vector2 shL = new Vector2(left.x + 31f * k, left.y + shY);
        Vector2 shR = new Vector2(right.x + 15f * k, right.y + shY);
        float gw = 35f * k, gh = 12f * k;
        if (gunP1 != null) GUI.DrawTexture(new Rect(shL.x, shL.y - gh / 2f, gw, gh), gunP1);
        if (gunCpu != null) GUI.DrawTextureWithTexCoords(new Rect(shR.x - gw, shR.y - gh / 2f, gw, gh), gunCpu, new Rect(1f, 0f, -1f, 1f));

        // 撃ち合う弾(画面の下を左右に飛ぶ)
        float startL = shL.x + gw, startR = shR.x - gw;
        float span = startR - startL;
        if (span <= 0f) return;
        for (int i = 0; i < 3; i++)
        {
            float tL = Mod(Time.unscaledTime * 0.55f + i / 3f, 1f);
            float tR = Mod(Time.unscaledTime * 0.45f + i / 3f + 0.15f, 1f);
            Shot(new Vector2(startL + span * tL, shL.y), cyan, unit);
            Shot(new Vector2(startR - span * tR, shR.y - unit * 0.15f), amber, unit);
        }
    }

    void Shot(Vector2 p, Color c, float unit)
    {
        float len = unit * 0.7f, th = unit * 0.14f;
        Fill(new Rect(p.x - len / 2f, p.y - th * 1.5f, len, th * 3f), new Color(c.r, c.g, c.b, 0.18f));
        Fill(new Rect(p.x - len / 2f, p.y - th / 2f, len, th), c);
    }

    // ===== あそびかた =====

    void DrawHowTo(float unit)
    {
        float contentW = Mathf.Min(Screen.width - unit * 1.6f, unit * 34f);
        float left = (Screen.width - contentW) / 2f;

        // 見出し
        buttonStyle.fontSize = Mathf.RoundToInt(unit * 1.1f);
        buttonStyle.normal.textColor = white;
        GUI.Label(new Rect(0, unit * 0.4f, Screen.width, unit * 1.4f), "HOW TO PLAY", buttonStyle);
        buttonStyle.fontSize = Mathf.RoundToInt(unit * 0.8f);

        // タブ
        float tabW = contentW / tabNames.Length;
        float tabY = unit * 2.0f, tabH = unit * 1.2f;
        for (int i = 0; i < tabNames.Length; i++)
        {
            Rect r = new Rect(left + i * tabW, tabY, tabW, tabH);
            bool active = tab == i;
            bool hover = r.Contains(Event.current.mousePosition);

            Fill(r, active ? card : bg);
            Fill(new Rect(r.x, r.yMax - unit * 0.1f, r.width, unit * 0.1f), active ? cyan : line);
            jpHead.alignment = TextAnchor.MiddleCenter;
            jpHead.normal.textColor = active ? cyan : (hover ? white : grey);
            GUI.Label(r, tabNames[i], jpHead);
            jpHead.alignment = TextAnchor.MiddleLeft;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) tab = i;
        }

        Rect body = new Rect(left, tabY + tabH + unit * 0.4f, contentW, Screen.height - (tabY + tabH + unit * 0.4f) - unit * 2.6f);
        if (tab == 0) DrawControls(body, unit);
        else if (tab == 1) DrawWeapons(body, unit);
        else DrawRules(body, unit);

        float bw = unit * 5f, bh = unit * 1.1f;
        if (Button(new Rect((Screen.width - bw) / 2f, Screen.height - unit * 2.2f, bw, bh), "BACK", false)) showHowTo = false;

        helpStyle.normal.textColor = grey;
        GUI.Label(new Rect(0, Screen.height - unit * 1.0f, Screen.width, unit * 0.8f),
                  "← → :タブ切り替え      ESC:もどる", helpStyle);
    }

    // カード1枚(上にアニメ、下に見出し・キー・説明)。アニメを描く場所を返す
    Rect DrawCard(Rect r, float unit, string head, string key, string text, Color accent, float previewRatio)
    {
        Fill(r, card);
        Border(r, line, 1f);

        Rect preview = new Rect(r.x + unit * 0.2f, r.y + unit * 0.2f, r.width - unit * 0.4f, r.height * previewRatio);
        Fill(preview, stage);

        float y = preview.yMax + unit * 0.15f;
        jpHead.normal.textColor = white;
        GUI.Label(new Rect(r.x + unit * 0.35f, y, r.width - unit * 0.7f, unit * 0.85f), head, jpHead);

        if (!string.IsNullOrEmpty(key))
        {
            Vector2 size = chipStyle.CalcSize(new GUIContent(key));
            float cw = size.x + unit * 0.5f;
            Rect chip = new Rect(r.xMax - unit * 0.35f - cw, y + unit * 0.1f, cw, unit * 0.65f);
            Border(chip, accent, 1f);
            chipStyle.normal.textColor = accent;
            GUI.Label(chip, key, chipStyle);
        }

        jpText.normal.textColor = grey;
        GUI.Label(new Rect(r.x + unit * 0.35f, y + unit * 0.85f, r.width - unit * 0.7f, r.yMax - y - unit * 0.9f), text, jpText);

        return preview;
    }

    void DrawControls(Rect body, float unit)
    {
        int cols = 4, rows = 2;
        float gap = unit * 0.35f;
        float cw = (body.width - gap * (cols - 1)) / cols;
        float ch = (body.height - gap * (rows - 1)) / rows;

        for (int i = 0; i < controls.GetLength(0); i++)
        {
            Rect r = new Rect(body.x + (i % cols) * (cw + gap), body.y + (i / cols) * (ch + gap), cw, ch);
            Rect p = DrawCard(r, unit, controls[i, 0], controls[i, 1], controls[i, 2], cyan, 0.52f);

            GUI.BeginGroup(p);
            AnimControl(i, p.width, p.height);
            GUI.EndGroup();
        }
    }

    void DrawWeapons(Rect body, float unit)
    {
        int cols = 3, rows = 2;
        float gap = unit * 0.35f;
        float cw = (body.width - gap * (cols - 1)) / cols;
        float ch = (body.height - gap * (rows - 1)) / rows;

        for (int i = 0; i < weaponNames.Length; i++)
        {
            Rect r = new Rect(body.x + (i % cols) * (cw + gap), body.y + (i / cols) * (ch + gap), cw, ch);
            string head = (i + 1) + "  " + weaponNames[i];
            Rect p = DrawCard(r, unit, head, "", weaponNotes[i], cyan, 0.5f);

            // 熱と間隔(見出しの右)
            float y = p.yMax + unit * 0.15f;
            float barW = unit * 2.4f;
            Rect bar = new Rect(r.xMax - unit * 0.35f - barW, y + unit * 0.36f, barW, unit * 0.16f);
            Fill(bar, line);
            Fill(new Rect(bar.x, bar.y, bar.width * weaponHeat[i] / 100f, bar.height), weaponHeat[i] >= 30f ? hot : cyan);
            jpText.normal.textColor = grey;
            jpText.alignment = TextAnchor.MiddleRight;
            jpText.fontSize = Mathf.RoundToInt(unit * 0.38f);
            GUI.Label(new Rect(bar.x - unit * 4.2f, y, unit * 4f, unit * 0.85f),
                      "熱 " + weaponHeat[i] + " / " + weaponInterval[i].ToString("0.##") + "秒", jpText);
            jpText.alignment = TextAnchor.UpperLeft;
            jpText.fontSize = Mathf.RoundToInt(unit * 0.46f);

            GUI.BeginGroup(p);
            AnimWeapon(i, p.width, p.height);
            GUI.EndGroup();
        }
    }

    void DrawRules(Rect body, float unit)
    {
        float gap = unit * 0.4f;
        float cw = (body.width - gap * 2f) / 3f;
        Color[] accents = { cyan, hot, red };

        for (int c = 0; c < 3; c++)
        {
            Rect r = new Rect(body.x + c * (cw + gap), body.y, cw, body.height);
            Fill(r, card);
            Border(r, line, 1f);
            Fill(new Rect(r.x, r.y, r.width, unit * 0.1f), accents[c]);

            Rect p = new Rect(r.x + unit * 0.2f, r.y + unit * 0.3f, r.width - unit * 0.4f, r.height * 0.46f);
            Fill(p, stage);
            GUI.BeginGroup(p);
            AnimRule(c, p.width, p.height);
            GUI.EndGroup();

            float y = p.yMax + unit * 0.25f;
            jpHead.normal.textColor = accents[c];
            GUI.Label(new Rect(r.x + unit * 0.4f, y, r.width - unit * 0.8f, unit * 0.9f), ruleHeads[c], jpHead);

            y += unit * 1.0f;
            float lh = (r.yMax - y - unit * 0.2f) / ruleLines[c].Length;
            for (int i = 0; i < ruleLines[c].Length; i++)
            {
                Fill(new Rect(r.x + unit * 0.45f, y + i * lh + unit * 0.22f, unit * 0.14f, unit * 0.14f), accents[c]);
                jpText.normal.textColor = white;
                GUI.Label(new Rect(r.x + unit * 0.8f, y + i * lh, r.width - unit * 1.2f, lh), ruleLines[c][i], jpText);
            }
        }
    }

    // ===== アニメーション(操作) =====

    void AnimControl(int i, float w, float h)
    {
        float floor = h * 0.84f;
        float s = h * 0.42f; // ロボットの高さ
        Fill(new Rect(0, floor, w, h - floor), plat);

        switch (i)
        {
            case 0: // 移動
                {
                    float x = w * 0.5f + Mathf.Sin(T * 1.8f) * w * 0.3f;
                    int face = Mathf.Cos(T * 1.8f) >= 0f ? 1 : -1;
                    DrawBot(x, floor, s, cyan, face, 1f, 0f);
                    Arrow(new Vector2(w * 0.5f - face * w * 0.08f, h * 0.18f), face, w * 0.16f, grey);
                    break;
                }
            case 1: // ジャンプ(2段)
                {
                    float t = Mod(T, 1.9f);
                    float y = 0f;
                    bool second = false;
                    const float v0 = 1f, g = 4f, t1 = 0.35f;
                    if (t < t1) y = v0 * t - g * t * t / 2f;
                    else
                    {
                        float y1 = v0 * t1 - g * t1 * t1 / 2f;
                        float u = t - t1;
                        y = Mathf.Max(0f, y1 + v0 * u - g * u * u / 2f);
                        second = u < 0.25f;
                    }
                    float py = floor - y * h * 1.5f;
                    DrawBot(w * 0.5f, py, s, cyan, 1, 1f, 0f);
                    if (second)
                    {
                        Ring(new Vector2(w * 0.5f, py + 2f), s * 0.35f, cyan, 0.8f);
                        jpHead.normal.textColor = cyan;
                        GUI.Label(new Rect(w * 0.62f, py - s * 0.9f, w * 0.4f, s * 0.6f), "×2", jpHead);
                    }
                    break;
                }
            case 2: // 壁キック
                {
                    float wallX = w * 0.86f;
                    Fill(new Rect(wallX, h * 0.1f, w * 0.06f, floor - h * 0.1f), plat);
                    float t = Mod(T, 2.2f) / 2.2f;
                    float x, y; int face = 1; bool kick = false;
                    if (t < 0.3f) { x = Mathf.Lerp(w * 0.25f, w * 0.72f, t / 0.3f); y = 0f; }
                    else if (t < 0.5f) { float u = (t - 0.3f) / 0.2f; x = Mathf.Lerp(w * 0.72f, wallX - s * 0.25f, u); y = Mathf.Sin(u * Mathf.PI * 0.5f) * 0.45f; }
                    else if (t < 0.85f)
                    {
                        float u = (t - 0.5f) / 0.35f;
                        x = Mathf.Lerp(wallX - s * 0.25f, w * 0.3f, u);
                        y = 0.45f + Mathf.Sin(u * Mathf.PI) * 0.25f - u * 0.45f;
                        face = -1; kick = u < 0.25f;
                    }
                    else { x = w * 0.3f; y = 0f; face = -1; }
                    float py = floor - Mathf.Max(0f, y) * h;
                    DrawBot(x, py, s, cyan, face, 1f, 0f);
                    if (kick) Ring(new Vector2(wallX, py - s * 0.4f), s * 0.3f, white, 0.9f);
                    break;
                }
            case 3: // ダッシュ
                {
                    float t = Mod(T, 2f) / 2f;
                    float x;
                    bool dashing = t > 0.35f && t < 0.55f;
                    if (t < 0.35f) x = Mathf.Lerp(w * 0.15f, w * 0.3f, t / 0.35f);
                    else if (t < 0.55f) x = Mathf.Lerp(w * 0.3f, w * 0.78f, (t - 0.35f) / 0.2f);
                    else x = w * 0.78f;
                    if (dashing)
                        for (int k = 3; k >= 1; k--)
                            DrawBot(x - k * s * 0.35f, floor, s, cyan, 1, 0.12f * (4 - k), 0f);
                    float fade = t > 0.9f ? 1f - (t - 0.9f) / 0.1f : 1f;
                    DrawBot(x, floor, s, cyan, 1, fade, 0f);
                    break;
                }
            case 4: // ねらう
                {
                    Vector2 c = new Vector2(w * 0.72f + Mathf.Cos(T * 1.3f) * w * 0.16f, h * 0.42f + Mathf.Sin(T * 2.1f) * h * 0.25f);
                    float bx = w * 0.25f;
                    Vector2 shoulder = new Vector2(bx, floor - s * 0.6f);
                    float ang = Mathf.Atan2(c.y - shoulder.y, c.x - shoulder.x) * Mathf.Rad2Deg;
                    DrawBot(bx, floor, s, cyan, 1, 1f, ang);
                    Crosshair(c, s * 0.18f, white);
                    DrawDots(shoulder, c, cyan, 0.25f);
                    break;
                }
            case 5: // 撃つ
                {
                    float bx = w * 0.18f;
                    DrawBot(bx, floor, s, cyan, 1, 1f, 0f);
                    float period = 0.7f;
                    float since = Mod(T, period);
                    for (int k = 0; k < 3; k++)
                    {
                        float age = since + k * period;
                        float x = bx + s * 0.6f + age * w * 0.55f;
                        if (x < w) Bullet(new Vector2(x, floor - s * 0.6f), cyan, s * 0.09f);
                    }
                    if (since < 0.12f) Fill(new Rect(bx + s * 0.55f, floor - s * 0.68f, s * 0.15f, s * 0.15f), white);
                    break;
                }
            case 6: // 武器を変える
                {
                    int cur = Mathf.FloorToInt(T / 0.9f) % 6;
                    float sw = w * 0.13f, sg = w * 0.02f;
                    float sx = (w - (sw * 6 + sg * 5)) / 2f;
                    for (int k = 0; k < 6; k++)
                    {
                        Rect r = new Rect(sx + k * (sw + sg), h * 0.12f, sw, h * 0.2f);
                        Fill(r, card);
                        Border(r, k == cur ? cyan : line, k == cur ? 2f : 1f);
                        chipStyle.normal.textColor = k == cur ? cyan : grey;
                        GUI.Label(r, (k + 1).ToString(), chipStyle);
                    }
                    // 持ち替えゲージ
                    float prog = Mathf.Clamp01(Mod(T, 0.9f) / 0.25f);
                    Rect cr = new Rect(sx + cur * (sw + sg), h * 0.12f, sw * prog, 3f);
                    Fill(cr, cyan);
                    DrawBot(w * 0.3f, floor, s, cyan, 1, 1f, 0f);
                    chipStyle.normal.textColor = white;
                    chipStyle.alignment = TextAnchor.MiddleLeft;
                    GUI.Label(new Rect(w * 0.45f, floor - s * 0.85f, w * 0.55f, s * 0.5f), weaponNames[cur], chipStyle);
                    chipStyle.alignment = TextAnchor.MiddleCenter;
                    break;
                }
            case 7: // ポーズ
                {
                    DrawBot(w * 0.3f, floor, s, cyan, 1, 0.35f, 0f);
                    DrawBot(w * 0.72f, floor, s, amber, -1, 0.35f, 0f);
                    float a = 0.6f + 0.4f * Mathf.Sin(T * 4f);
                    float bw = w * 0.05f, bh = h * 0.3f;
                    Fill(new Rect(w * 0.5f - bw * 1.5f, h * 0.18f, bw, bh), new Color(white.r, white.g, white.b, a));
                    Fill(new Rect(w * 0.5f + bw * 0.5f, h * 0.18f, bw, bh), new Color(white.r, white.g, white.b, a));
                    break;
                }
        }
    }

    // ===== アニメーション(武器) =====

    void AnimWeapon(int i, float w, float h)
    {
        float floor = h * 0.84f;
        float s = h * 0.38f;
        Fill(new Rect(0, floor, w, h - floor), plat);

        float bx = w * 0.14f;
        Vector2 muzzle = new Vector2(bx + s * 0.62f, floor - s * 0.6f);
        float tx = w * 0.86f;

        switch (i)
        {
            case 0: // NORMAL:まっすぐ速い
                {
                    DrawBot(bx, floor, s, cyan, 1, 1f, 0f);
                    DrawBot(tx, floor, s, amber, -1, 1f, 0f);
                    float period = 0.35f;
                    for (int k = 0; k < 4; k++)
                    {
                        float age = Mod(T, period) + k * period;
                        float x = muzzle.x + age * w * 0.9f;
                        if (x < tx - s * 0.3f) Bullet(new Vector2(x, muzzle.y), cyan, s * 0.08f);
                    }
                    break;
                }
            case 1: // BOUND:床で跳ねる
                {
                    // 相手は上の足場にいる
                    Rect p = new Rect(w * 0.62f, h * 0.35f, w * 0.34f, h * 0.05f);
                    Fill(p, plat);
                    DrawBot(bx, floor, s * 0.9f, cyan, 1, 1f, -20f);
                    DrawBot(w * 0.8f, p.y, s * 0.8f, amber, -1, 1f, 0f);
                    float t = Mod(T, 1.6f) / 1.6f;
                    float x = Mathf.Lerp(muzzle.x, w * 0.78f, t);
                    // 2回はねて、上の相手へ
                    float bounce = Mathf.Abs(Mathf.Cos(t * Mathf.PI * 1.5f + Mathf.PI * 0.5f));
                    float y = floor - s * 0.12f - bounce * (floor - h * 0.3f) * (0.4f + t * 0.6f);
                    Bullet(new Vector2(x, y), cyan, s * 0.09f);
                    for (int k = 1; k <= 4; k++)
                    {
                        float tt = Mathf.Max(0f, t - k * 0.025f);
                        float xx = Mathf.Lerp(muzzle.x, w * 0.78f, tt);
                        float bb = Mathf.Abs(Mathf.Cos(tt * Mathf.PI * 1.5f + Mathf.PI * 0.5f));
                        float yy = floor - s * 0.12f - bb * (floor - h * 0.3f) * (0.4f + tt * 0.6f);
                        Bullet(new Vector2(xx, yy), new Color(cyan.r, cyan.g, cyan.b, 0.5f - k * 0.1f), s * 0.07f);
                    }
                    break;
                }
            case 2: // TRACKING:相手に向かって曲がる
                {
                    float ty = floor - Mathf.Abs(Mathf.Sin(T * 1.5f)) * h * 0.45f;
                    DrawBot(bx, floor, s, cyan, 1, 1f, 0f);
                    DrawBot(tx, ty, s, amber, -1, 1f, 0f);
                    float t = Mod(T, 1.4f) / 1.4f;
                    Vector2 target = new Vector2(tx - s * 0.2f, ty - s * 0.5f);
                    Vector2 ctrl = new Vector2(w * 0.5f, muzzle.y);
                    for (int k = 0; k < 6; k++)
                    {
                        float tt = Mathf.Max(0f, t - k * 0.03f);
                        Vector2 q = Bezier(muzzle, ctrl, target, tt);
                        Bullet(q, new Color(cyan.r, cyan.g, cyan.b, k == 0 ? 1f : 0.45f - k * 0.07f), s * (k == 0 ? 0.1f : 0.07f));
                    }
                    break;
                }
            case 3: // SHOTGUN:5発が広がる
                {
                    DrawBot(bx, floor, s, cyan, 1, 1f, 0f);
                    DrawBot(w * 0.6f, floor, s, amber, -1, 1f, 0f);
                    float age = Mod(T, 1.1f);
                    float alpha = Mathf.Clamp01(1f - age / 0.45f);
                    for (int k = 0; k < 5; k++)
                    {
                        float a = (-14f + k * 7f) * Mathf.Deg2Rad;
                        Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                        Vector2 q = muzzle + d * age * w * 0.9f;
                        if (alpha > 0f) Bullet(q, new Color(cyan.r, cyan.g, cyan.b, alpha), s * 0.08f);
                    }
                    break;
                }
            case 4: // GRENADE:山なり → 爆発
                {
                    DrawBot(bx, floor, s, cyan, 1, 1f, -30f);
                    DrawBot(tx, floor, s, amber, -1, 1f, 0f);
                    float t = Mod(T, 2f) / 2f;
                    Vector2 land = new Vector2(tx - s * 0.2f, floor - s * 0.1f);
                    if (t < 0.6f)
                    {
                        float u = t / 0.6f;
                        float x = Mathf.Lerp(muzzle.x, land.x, u);
                        float y = Mathf.Lerp(muzzle.y, land.y, u) - Mathf.Sin(u * Mathf.PI) * h * 0.5f;
                        Bullet(new Vector2(x, y), amber, s * 0.14f);
                    }
                    else
                    {
                        float u = (t - 0.6f) / 0.4f;
                        Ring(land, s * (0.3f + u * 0.9f), hot, 1f - u);
                        Ring(land, s * (0.2f + u * 0.6f), amber, (1f - u) * 0.7f);
                    }
                    break;
                }
            case 5: // ROUTE:線を描いて、その通りに飛ぶ
                {
                    DrawBot(bx, floor, s, cyan, 1, 1f, 0f);
                    DrawBot(tx, floor, s, amber, -1, 1f, 0f);
                    Vector2[] path =
                    {
                    muzzle,
                    new Vector2(w * 0.35f, h * 0.2f),
                    new Vector2(w * 0.6f, h * 0.15f),
                    new Vector2(w * 0.72f, h * 0.45f),
                    new Vector2(tx - s * 0.2f, floor - s * 0.55f),
                };
                    float t = Mod(T, 3f) / 3f;
                    if (t < 0.45f)
                    {
                        float u = t / 0.45f;
                        Vector2 tip = DrawPath(path, u, cyan, 0.9f);
                        Crosshair(tip, s * 0.15f, white);
                    }
                    else if (t < 0.85f)
                    {
                        float u = (t - 0.45f) / 0.4f;
                        DrawPath(path, 1f, cyan, 0.35f * (1f - u));
                        Bullet(PathPoint(path, u), cyan, s * 0.11f);
                    }
                    else
                    {
                        float u = (t - 0.85f) / 0.15f;
                        Ring(path[path.Length - 1], s * (0.2f + u * 0.4f), white, 1f - u);
                    }
                    break;
                }
        }
    }

    // ===== アニメーション(ルール) =====

    void AnimRule(int c, float w, float h)
    {
        float floor = h * 0.8f;
        float s = h * 0.32f;

        switch (c)
        {
            case 0: // ストックと KO
                {
                    Fill(new Rect(w * 0.08f, floor, w * 0.62f, h * 0.06f), plat);
                    float t = Mod(T, 3f) / 3f;
                    int stocks = t < 0.55f ? 3 : 2;

                    // ストックの四角
                    float sq = h * 0.07f;
                    for (int k = 0; k < 3; k++)
                    {
                        Rect r = new Rect(w * 0.06f + k * sq * 1.5f, h * 0.08f, sq, sq);
                        if (k < stocks) Fill(r, cyan); else Border(r, line, 1f);
                    }

                    DrawBot(w * 0.2f, floor, s, amber, 1, 1f, 0f);
                    if (t < 0.25f)
                    {
                        float u = t / 0.25f;
                        Bullet(new Vector2(Mathf.Lerp(w * 0.28f, w * 0.55f, u), floor - s * 0.6f), amber, s * 0.1f);
                        DrawBot(w * 0.6f, floor, s, cyan, -1, 1f, 0f);
                    }
                    else if (t < 0.55f)
                    {
                        // 吹き飛ぶ
                        float u = (t - 0.25f) / 0.3f;
                        float x = w * 0.6f + u * w * 0.55f;
                        float y = floor - Mathf.Sin(u * Mathf.PI * 0.6f) * h * 0.4f;
                        DrawBot(x, y, s, cyan, -1, 1f, 0f);
                    }
                    else if (t < 0.85f)
                    {
                        float u = (t - 0.55f) / 0.3f;
                        jpHead.alignment = TextAnchor.MiddleCenter;
                        jpHead.fontSize = Mathf.RoundToInt(h * 0.2f);
                        jpHead.normal.textColor = new Color(cyan.r, cyan.g, cyan.b, 1f - u * 0.5f);
                        GUI.Label(new Rect(w * 0.5f, h * 0.15f - u * h * 0.05f, w * 0.5f, h * 0.3f), "KO!", jpHead);
                        jpHead.fontSize = Mathf.RoundToInt(Screen.height / 20f * 0.62f);
                        jpHead.alignment = TextAnchor.MiddleLeft;
                    }
                    else
                    {
                        // 上から戻ってくる
                        float u = (t - 0.85f) / 0.15f;
                        DrawBot(w * 0.45f, Mathf.Lerp(h * 0.1f, floor, u), s, cyan, -1, 0.5f + 0.5f * Mathf.Sin(T * 30f), 0f);
                    }
                    break;
                }
            case 1: // 熱とオーバーヒート
                {
                    Fill(new Rect(0, floor, w, h - floor), plat);
                    float t = Mod(T, 5f);
                    int shots = Mathf.Min(5, Mathf.FloorToInt(t / 0.45f) + 1);
                    bool over = t >= 0.45f * 5f;
                    float heat = over ? 100f : shots * 20f;

                    DrawBot(w * 0.2f, floor, s, cyan, 1, 1f, 0f);
                    if (!over)
                    {
                        float age = Mod(t, 0.45f);
                        Bullet(new Vector2(w * 0.2f + s * 0.6f + age * w * 1.4f, floor - s * 0.6f), cyan, s * 0.09f);
                    }

                    Rect bar = new Rect(w * 0.1f, h * 0.12f, w * 0.8f, h * 0.08f);
                    Fill(bar, line);
                    Fill(new Rect(bar.x, bar.y, bar.width * heat / 100f, bar.height), over ? red : (heat >= 70f ? hot : cyan));

                    chipStyle.alignment = TextAnchor.MiddleLeft;
                    if (over)
                    {
                        int left = Mathf.CeilToInt(30f - (t - 2.25f) / 2.75f * 30f);
                        float blink = 0.6f + 0.4f * Mathf.Sin(T * 10f);
                        chipStyle.normal.textColor = new Color(red.r, red.g, red.b, blink);
                        GUI.Label(new Rect(bar.x, bar.yMax + h * 0.04f, w, h * 0.15f), "OVERHEAT  " + Mathf.Max(0, left), chipStyle);
                    }
                    else
                    {
                        chipStyle.normal.textColor = grey;
                        GUI.Label(new Rect(bar.x, bar.yMax + h * 0.04f, w, h * 0.15f), "HEAT  " + Mathf.RoundToInt(heat), chipStyle);
                    }
                    chipStyle.alignment = TextAnchor.MiddleCenter;
                    break;
                }
            case 2: // 部位破壊
                {
                    Fill(new Rect(0, floor, w, h - floor), plat);
                    float big = h * 0.62f;
                    float cx = w * 0.38f;
                    DrawBot(cx, floor, big, cyan, 1, 1f, 0f);

                    int part = Mathf.FloorToInt(T / 1.4f) % 3;
                    float u = Mod(T, 1.4f) / 1.4f;
                    float k = big / 84f;
                    // 頭 / 腕 / 脚 の場所
                    Rect[] zones =
                    {
                    new Rect(cx - 11 * k, floor - 84 * k, 24 * k, 18 * k),
                    new Rect(cx + 10 * k, floor - 56 * k, 48 * k, 12 * k),
                    new Rect(cx - 14 * k, floor - 30 * k, 28 * k, 30 * k),
                };
                    string[] names = { "頭  ×1.5", "腕", "脚" };

                    Rect z = zones[part];
                    Color zc = u > 0.5f ? red : hot;
                    Fill(z, new Color(zc.r, zc.g, zc.b, 0.85f));
                    if (u < 0.4f)
                    {
                        float hx = Mathf.Lerp(w, z.center.x, u / 0.4f);
                        Bullet(new Vector2(hx, z.center.y), amber, big * 0.05f);
                    }
                    else Ring(z.center, big * (0.1f + (u - 0.4f) * 0.3f), white, Mathf.Clamp01(1f - (u - 0.4f) * 2f));

                    jpHead.normal.textColor = zc;
                    GUI.Label(new Rect(w * 0.66f, z.center.y - h * 0.08f, w * 0.34f, h * 0.16f), names[part], jpHead);
                    if (u > 0.5f)
                    {
                        chipStyle.normal.textColor = red;
                        chipStyle.alignment = TextAnchor.MiddleLeft;
                        GUI.Label(new Rect(w * 0.66f, z.center.y + h * 0.06f, w * 0.34f, h * 0.12f), "BROKEN", chipStyle);
                        chipStyle.alignment = TextAnchor.MiddleCenter;
                    }
                    break;
                }
        }
    }

    // ===== 描画の部品 =====

    // 箱型のロボット。足元が (x, feetY)、高さが s。gunAngle は銃の角度(0 で正面)
    void DrawBot(float x, float feetY, float s, Color c, int face, float alpha, float gunAngle)
    {
        float k = s / 84f;
        Color body = WithA(c, alpha);
        Color mid = WithA(c * 0.6f, alpha);
        Color dark = WithA(c * 0.35f, alpha);

        void R(float x0, float y0, float x1, float y1, Color col)
        {
            float a = x + x0 * k * face, b = x + x1 * k * face;
            Fill(new Rect(Mathf.Min(a, b), feetY - y1 * k, Mathf.Abs(b - a), (y1 - y0) * k), col);
        }

        R(-14, 0, -4, 30, mid);
        R(4, 0, 14, 30, mid);
        R(-20, 28, 20, 66, body);
        R(-6, 44, 10, 56, dark);
        R(-28, 38, -20, 62, mid);
        R(-11, 66, 13, 84, body);
        R(2, 72, 15, 78, WithA(bg, alpha));
        R(5, 73, 13, 77, WithA(white, alpha));

        // 銃(肩を中心に回す)
        Vector2 pivot = new Vector2(x + 12 * k * face, feetY - 50 * k);
        Matrix4x4 m = GUI.matrix;
        float ang = face > 0 ? gunAngle : 180f + gunAngle;
        if (face < 0 && gunAngle == 0f) ang = 180f;
        GUIUtility.RotateAroundPivot(ang, pivot);
        Fill(new Rect(pivot.x, pivot.y - 5 * k, 46 * k, 10 * k), dark);
        GUI.matrix = m;
    }

    void Bullet(Vector2 p, Color c, float r)
    {
        Fill(new Rect(p.x - r * 2f, p.y - r * 2f, r * 4f, r * 4f), new Color(c.r, c.g, c.b, c.a * 0.18f));
        Fill(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), c);
    }

    void Ring(Vector2 c, float r, Color col, float alpha)
    {
        if (alpha <= 0f) return;
        Color cc = new Color(col.r, col.g, col.b, alpha);
        int n = 24;
        float d = Mathf.Max(2f, r * 0.12f);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Fill(new Rect(c.x + Mathf.Cos(a) * r - d / 2f, c.y + Mathf.Sin(a) * r - d / 2f, d, d), cc);
        }
    }

    void Crosshair(Vector2 c, float r, Color col)
    {
        Fill(new Rect(c.x - r, c.y - 1f, r * 2f, 2f), col);
        Fill(new Rect(c.x - 1f, c.y - r, 2f, r * 2f), col);
    }

    void Arrow(Vector2 from, int dir, float len, Color col)
    {
        Fill(new Rect(dir > 0 ? from.x : from.x - len, from.y - 1.5f, len, 3f), col);
        float tipX = from.x + dir * len;
        for (int i = 0; i < 5; i++)
            Fill(new Rect(tipX - dir * i * 2f - 1f, from.y - i * 1.5f - 1f, 2f, i * 3f + 2f), col);
    }

    void DrawDots(Vector2 a, Vector2 b, Color c, float alpha)
    {
        float len = Vector2.Distance(a, b);
        int n = Mathf.Max(2, Mathf.RoundToInt(len / 8f));
        for (int i = 0; i < n; i += 2)
        {
            Vector2 p = Vector2.Lerp(a, b, i / (float)n);
            Fill(new Rect(p.x - 1.5f, p.y - 1.5f, 3f, 3f), new Color(c.r, c.g, c.b, alpha));
        }
    }

    // 折れ線を u(0〜1)の所まで描いて、先端の位置を返す
    Vector2 DrawPath(Vector2[] pts, float u, Color c, float alpha)
    {
        Vector2 tip = pts[0];
        int steps = 60;
        int upto = Mathf.RoundToInt(steps * u);
        for (int i = 0; i <= upto; i++)
        {
            Vector2 p = PathPoint(pts, i / (float)steps);
            Fill(new Rect(p.x - 1.5f, p.y - 1.5f, 3f, 3f), new Color(c.r, c.g, c.b, alpha));
            tip = p;
        }
        return tip;
    }

    // なめらかな曲線(Catmull-Rom)の上の点
    Vector2 PathPoint(Vector2[] p, float u)
    {
        int segs = p.Length - 1;
        float f = Mathf.Clamp01(u) * segs;
        int i = Mathf.Min(segs - 1, Mathf.FloorToInt(f));
        float t = f - i;
        Vector2 p0 = p[Mathf.Max(0, i - 1)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(segs, i + 2)];
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        return Vector2.Lerp(Vector2.Lerp(a, b, t), Vector2.Lerp(b, c, t), t);
    }

    static float Mod(float a, float b) => a - b * Mathf.Floor(a / b);
    static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // ===== 共通 =====

    bool Button(Rect r, string label, bool primary)
    {
        bool hover = r.Contains(Event.current.mousePosition);

        if (primary)
        {
            Fill(r, hover ? white : cyan);
            buttonStyle.normal.textColor = new Color32(0x03, 0x14, 0x17, 0xFF);
        }
        else
        {
            Fill(r, card);
            Border(r, hover ? cyan : line, hover ? 2f : 1f);
            buttonStyle.normal.textColor = hover ? cyan : white;
        }

        GUI.Label(r, label, buttonStyle);
        bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
        if (clicked) SoundManager.Play("ui");
        return clicked;
    }

    void LoadScene(string sceneName)
    {
        if (Application.CanStreamedLevelBeLoaded(sceneName))
            SceneManager.LoadScene(sceneName);
        else
            Debug.LogWarning(sceneName + " が Build Profiles の Scene List に入っていません");
    }

    void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    void Border(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }
}