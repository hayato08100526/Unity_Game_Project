using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// CPU 対戦の試合を管理する(カウントダウン、場外KO、ストック、リスポーン、時間切れ、勝敗)
// シーンに空のオブジェクトを作って付ける
public class MatchManager : MonoBehaviour
{
    public static MatchManager Instance { get; private set; }

    [Header("戦う2人(空なら「Player」「CPU」という名前で探す)")]
    public HealthSystem player;
    public HealthSystem cpu;

    [Header("ルール")]
    public int stockCount = 3;
    public float matchTime = 180f;
    public float countdownTime = 3f;
    public float respawnDelay = 1.5f;
    public float invincibleTime = 2f;

    [Header("リスポーン地点(最上段の足場の上)")]
    public Vector2 respawnPoint = new Vector2(0f, 8.5f);

    [Header("タイトルに戻る")]
    public string titleSceneName = "MainMenu";

    [Header("画面表示")]
    [Tooltip("HUD に使うフォント(Fonts フォルダの Oxanium-Bold など .ttf のもの)。空なら標準のフォント")]
    public Font hudFont;

    [Header("頭の上のマーカー")]
    [Tooltip("ロボットの足元(中心)から、マーカーを出す高さ")]
    public float markerHeight = 1.6f;
    public bool showMarkers = true;

    [Header("場外ライン(ここを越えたらKO)")]
    public float blastLeft = -16f;
    public float blastRight = 16f;
    public float blastBottom = -8f;
    public float blastTop = 16f;

    private enum State { SelectDifficulty, Countdown, Playing, Result }
    private State state = State.SelectDifficulty;

    // ポーズ中か(Esc で切り替え)
    private bool paused = false;

    // シーンを読み込み直しても覚えておく(再戦用)
    private static int savedDifficulty = -1;
    private static bool skipSelect = false;
    private static readonly string[] difficultyNames = { "EASY", "NORMAL", "HARD" };

    private int playerStocks;
    private int cpuStocks;
    private bool playerWaiting;
    private bool cpuWaiting;
    private float timeLeft;
    private float countdownLeft;
    private float fightTextTimer;
    private string resultText = "";

    private GUIStyle bigStyle;
    private GUIStyle midStyle;
    private GUIStyle smallStyle;
    private GUIStyle tinyStyle;

    private WeaponSystem playerWeapon;
    private WeaponSystem cpuWeapon;

    // 「LEG BROKEN」などの文字を頭上に出す
    private class Popup
    {
        public Transform who;
        public string text;
        public float startTime;
        public Color color;
        public float offsetX;
        public bool small;
    }
    private readonly System.Collections.Generic.List<Popup> popups = new System.Collections.Generic.List<Popup>();
    private const float PopupDuration = 1.4f;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (player != null) player.OnPartBroken -= HandlePartBroken;
        if (cpu != null) cpu.OnPartBroken -= HandlePartBroken;
    }

    void HandlePartBroken(HealthSystem who, HealthSystem.Part part)
    {
        string text = part == HealthSystem.Part.Head ? "HEAD BROKEN"
                    : part == HealthSystem.Part.Arm ? "ARM BROKEN"
                    : "LEG BROKEN";
        ShowPopup(who.transform, text, new Color32(0xFF, 0x8A, 0x3D, 0xFF), false);
    }

    // 頭上に文字を出す(ダメージの数字は small = true で小さく、少し横にずらす)
    public void ShowPopup(Transform who, string text, Color color, bool small)
    {
        if (who == null) return;
        popups.Add(new Popup
        {
            who = who,
            text = text,
            startTime = Time.time,
            color = color,
            small = small,
            offsetX = small ? Random.Range(-0.5f, 0.5f) : 0f
        });
    }

    void Start()
    {
        Time.timeScale = 1f;

        if (player == null) player = FindFighter("Player");
        if (cpu == null) cpu = FindFighter("CPU");

        playerWeapon = FindWeapon(player);
        cpuWeapon = FindWeapon(cpu);

        if (player != null) player.OnPartBroken += HandlePartBroken;
        if (cpu != null) cpu.OnPartBroken += HandlePartBroken;

        playerStocks = stockCount;
        cpuStocks = stockCount;
        timeLeft = matchTime;
        countdownLeft = countdownTime;

        SetControl(player, false);
        SetControl(cpu, false);

        // 「同じ強さで再戦」のときは、強さ選びを飛ばす
        if (skipSelect && savedDifficulty >= 0)
        {
            skipSelect = false;
            ChooseDifficulty(savedDifficulty);
        }
        else
        {
            state = State.SelectDifficulty;
        }
    }

    void ChooseDifficulty(int index)
    {
        savedDifficulty = index;

        var brain = cpu != null ? cpu.GetComponent<CpuController>() : null;
        if (brain != null) brain.ApplyDifficulty((CpuController.Difficulty)index);

        state = State.Countdown;
        countdownLeft = countdownTime;
    }

    HealthSystem FindFighter(string objectName)
    {
        GameObject go = GameObject.Find(objectName);
        return go != null ? go.GetComponent<HealthSystem>() : null;
    }

    // 表示に使う銃(有効になっている方)を探す
    WeaponSystem FindWeapon(HealthSystem fighter)
    {
        if (fighter == null) return null;
        WeaponSystem first = null;
        foreach (var w in fighter.GetComponentsInChildren<WeaponSystem>(true))
        {
            if (first == null) first = w;
            if (w.gameObject.activeInHierarchy) return w;
        }
        return first;
    }

    void Update()
    {
        // ===== ポーズ =====
        if (state == State.Countdown || state == State.Playing)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
            {
                if (paused) Resume(); else Pause();
                return;
            }
            if (paused)
            {
                if (Input.GetKeyDown(KeyCode.R)) Reload(true);
                else if (Input.GetKeyDown(KeyCode.T)) GoToTitle();
                return; // ポーズ中は試合を進めない
            }
        }

        switch (state)
        {
            case State.SelectDifficulty:
                if (Input.GetKeyDown(KeyCode.Alpha1)) ChooseDifficulty(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) ChooseDifficulty(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) ChooseDifficulty(2);
                break;

            case State.Countdown:
                countdownLeft -= Time.deltaTime;
                if (countdownLeft <= 0f)
                {
                    state = State.Playing;
                    fightTextTimer = 0.8f;
                    SetControl(player, true);
                    SetControl(cpu, true);
                }
                break;

            case State.Playing:
                if (fightTextTimer > 0f) fightTextTimer -= Time.deltaTime;

                timeLeft -= Time.deltaTime;
                if (timeLeft <= 0f)
                {
                    timeLeft = 0f;
                    TimeUp();
                    break;
                }

                CheckKO(player, true);
                CheckKO(cpu, false);
                break;

            case State.Result:
                if (Input.GetKeyDown(KeyCode.R)) Reload(true);   // 同じ強さで再戦
                else if (Input.GetKeyDown(KeyCode.D)) Reload(false); // 強さを選び直す
                else if (Input.GetKeyDown(KeyCode.T)) GoToTitle();
                break;
        }
    }

    void Pause()
    {
        paused = true;
        Time.timeScale = 0f;
        SetControl(player, false);
        SetControl(cpu, false);
    }

    void Resume()
    {
        paused = false;
        Time.timeScale = 1f;
        // カウントダウン中に止めたときは、まだ動けないままにする
        if (state == State.Playing)
        {
            SetControl(player, true);
            SetControl(cpu, true);
        }
    }

    void GoToTitle()
    {
        if (!Application.CanStreamedLevelBeLoaded(titleSceneName)) return;
        paused = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName);
    }

    void Reload(bool sameDifficulty)
    {
        paused = false;
        skipSelect = sameDifficulty;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // 場外に出たか、HP が0になったら KO
    void CheckKO(HealthSystem fighter, bool isPlayer)
    {
        if (fighter == null) return;
        if (isPlayer ? playerWaiting : cpuWaiting) return;

        Vector3 p = fighter.transform.position;
        bool outOfBounds = p.x < blastLeft || p.x > blastRight || p.y < blastBottom || p.y > blastTop;
        bool noHealth = fighter.currentHealth <= 0;

        if (outOfBounds || noHealth) KnockOut(fighter, isPlayer);
    }

    void KnockOut(HealthSystem fighter, bool isPlayer)
    {
        // KO の手ごたえ:大きく揺れて「KO!」
        CameraFollow.Shake(0.5f, 0.35f);
        ShowPopup(fighter.transform, "KO!", isPlayer ? new Color32(0x4F, 0xE3, 0xF0, 0xFF) : new Color32(0xFF, 0xB5, 0x47, 0xFF), false);

        fighter.KnockOut();

        if (isPlayer)
        {
            playerStocks--;
            playerWaiting = true;
        }
        else
        {
            cpuStocks--;
            cpuWaiting = true;
        }

        if (playerStocks <= 0) { EndMatch("YOU LOSE"); return; }
        if (cpuStocks <= 0) { EndMatch("YOU WIN"); return; }

        StartCoroutine(RespawnAfterDelay(fighter, isPlayer));
    }

    IEnumerator RespawnAfterDelay(HealthSystem fighter, bool isPlayer)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (state != State.Playing) yield break;

        fighter.Respawn(respawnPoint, invincibleTime);
        if (isPlayer) playerWaiting = false;
        else cpuWaiting = false;
    }

    void TimeUp()
    {
        if (playerStocks != cpuStocks)
        {
            EndMatch(playerStocks > cpuStocks ? "YOU WIN" : "YOU LOSE");
            return;
        }

        int playerHp = player != null ? player.currentHealth : 0;
        int cpuHp = cpu != null ? cpu.currentHealth : 0;

        if (playerHp > cpuHp) EndMatch("YOU WIN");
        else if (playerHp < cpuHp) EndMatch("YOU LOSE");
        else EndMatch("DRAW");
    }

    void EndMatch(string text)
    {
        state = State.Result;
        resultText = text;
        SetControl(player, false);
        SetControl(cpu, false);
    }

    // 操作の ON / OFF(カウントダウン中とリザルト中は動けない)
    void SetControl(HealthSystem fighter, bool on)
    {
        if (fighter == null) return;

        var pc = fighter.GetComponent<PlayerController>();
        if (pc != null) pc.enabled = on;

        foreach (var w in fighter.GetComponentsInChildren<WeaponSystem>(true)) w.enabled = on;

        var brain = fighter.GetComponent<CpuController>();
        if (brain != null) brain.enabled = on;

        var body = fighter.GetComponent<Rigidbody2D>();
        if (body != null && !on) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
    }

    // ===== 仮の画面表示(木曜にちゃんとした HUD に置き換える) =====

    void OnGUI()
    {
        if (bigStyle == null)
        {
            bigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            midStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            smallStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            tinyStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };

            if (hudFont != null)
            {
                bigStyle.font = hudFont;
                midStyle.font = hudFont;
                smallStyle.font = hudFont;
                tinyStyle.font = hudFont;
            }
        }

        float unit = Screen.height / 20f;
        bigStyle.fontSize = Mathf.RoundToInt(unit * 2.4f);
        midStyle.fontSize = Mathf.RoundToInt(unit * 1.0f);
        smallStyle.fontSize = Mathf.RoundToInt(unit * 0.7f);
        tinyStyle.fontSize = Mathf.RoundToInt(unit * 0.42f);

        Color cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
        Color amber = new Color32(0xFF, 0xB5, 0x47, 0xFF);
        Color white = new Color32(0xE6, 0xED, 0xF3, 0xFF);

        // ===== HUD(試合プレビューのデザイン) =====
        float k = Screen.height / 720f;   // 1280x720 で作ったデザインを画面の大きさに合わせる
        DrawTimer(k, amber, white);

        string cpuSub = savedDifficulty >= 0 ? difficultyNames[savedDifficulty] : "";
        DrawHudPanel(false, k, "P1", "YOU", "", playerStocks, player, playerWaiting, playerWeapon,
                     cyan, new Color32(0x03, 0x14, 0x17, 0xFF));
        DrawHudPanel(true, k, "P2", "CPU", cpuSub, cpuStocks, cpu, cpuWaiting, cpuWeapon,
                     amber, new Color32(0x1F, 0x13, 0x00, 0xFF));

        // 頭の上のマーカー(画面の外にいるときは、ふちに矢印)
        if (showMarkers && state != State.SelectDifficulty)
        {
            DrawMarker(player, playerWaiting, "P1", cyan, new Color32(0x03, 0x14, 0x17, 0xFF), unit);
            DrawMarker(cpu, cpuWaiting, "CPU", amber, new Color32(0x1F, 0x13, 0x00, 0xFF), unit);
        }

        // 部位が壊れたときの文字(頭上に出して、上に浮かびながら消える)
        DrawPopups(unit);

        // カウントダウン / FIGHT / 結果
        Rect center = new Rect(0, Screen.height * 0.3f, Screen.width, unit * 3f);
        if (state == State.SelectDifficulty)
        {
            DrawDifficultySelect(unit, cyan, white);
        }
        else if (state == State.Countdown)
        {
            bigStyle.normal.textColor = white;
            GUI.Label(center, Mathf.CeilToInt(countdownLeft).ToString(), bigStyle);
        }
        else if (state == State.Playing && fightTextTimer > 0f)
        {
            bigStyle.normal.textColor = cyan;
            GUI.Label(center, "FIGHT", bigStyle);
        }
        else if (state == State.Result)
        {
            bigStyle.normal.textColor = resultText == "YOU WIN" ? cyan : (resultText == "YOU LOSE" ? amber : white);
            GUI.Label(center, resultText, bigStyle);
            midStyle.normal.textColor = white;
            GUI.Label(new Rect(0, center.yMax, Screen.width, unit * 1.2f), "R : REMATCH    D : CHANGE LEVEL    T : TITLE", midStyle);
        }

        // ポーズ画面(一番上に重ねる)
        if (paused) DrawPauseMenu(unit, cyan, white);
    }

    void DrawPauseMenu(float unit, Color accent, Color white)
    {
        Color card = new Color32(0x0D, 0x11, 0x17, 0xFF);
        Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);

        // 画面全体を暗くする
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color32(0x07, 0x09, 0x0D, 0xC8));

        bigStyle.normal.textColor = white;
        GUI.Label(new Rect(0, Screen.height * 0.16f, Screen.width, unit * 3f), "PAUSE", bigStyle);

        string[] labels = { "RESUME   [ESC]", "RETRY   [R]", "TITLE   [T]" };
        float w = unit * 9f, h = unit * 1.6f, gap = unit * 0.4f;
        float x = (Screen.width - w) / 2f;
        float y0 = Screen.height * 0.42f;

        for (int i = 0; i < labels.Length; i++)
        {
            Rect r = new Rect(x, y0 + i * (h + gap), w, h);
            bool hover = r.Contains(Event.current.mousePosition);

            Fill(r, card);
            Border(r, hover ? accent : line, hover ? 2f : 1f);
            midStyle.normal.textColor = hover ? accent : white;
            GUI.Label(r, labels[i], midStyle);

            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (i == 0) Resume();
                else if (i == 1) Reload(true);
                else GoToTitle();
            }
        }
    }

    void DrawDifficultySelect(float unit, Color accent, Color white)
    {
        Color panel = new Color32(0x07, 0x09, 0x0D, 0xD8);
        Color card = new Color32(0x0D, 0x11, 0x17, 0xFF);
        Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);

        // 画面全体を少し暗くする
        Fill(new Rect(0, 0, Screen.width, Screen.height), panel);

        midStyle.normal.textColor = white;
        GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, unit * 1.4f), "SELECT CPU LEVEL", midStyle);

        float w = unit * 6f, h = unit * 2.4f, gap = unit * 0.6f;
        float total = w * 3 + gap * 2;
        float x0 = (Screen.width - total) / 2f;
        float y0 = Screen.height * 0.38f;

        string[] notes = { "Slow and careless", "A fair fight", "Fast and accurate" };

        for (int i = 0; i < 3; i++)
        {
            Rect r = new Rect(x0 + i * (w + gap), y0, w, h);
            bool hover = r.Contains(Event.current.mousePosition);

            Fill(r, card);
            Border(r, hover ? accent : line, hover ? 2f : 1f);

            midStyle.normal.textColor = hover ? accent : white;
            GUI.Label(new Rect(r.x, r.y + h * 0.12f, w, h * 0.45f), (i + 1) + "  " + difficultyNames[i], midStyle);

            tinyStyle.alignment = TextAnchor.MiddleCenter;
            tinyStyle.normal.textColor = new Color(0.55f, 0.6f, 0.66f);
            GUI.Label(new Rect(r.x, r.y + h * 0.58f, w, h * 0.3f), notes[i], tinyStyle);
            tinyStyle.alignment = TextAnchor.MiddleLeft;

            // クリックでも選べる
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) ChooseDifficulty(i);
        }

        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = new Color(0.55f, 0.6f, 0.66f);
        GUI.Label(new Rect(0, y0 + h + unit * 0.8f, Screen.width, unit), "Click a card or press 1 / 2 / 3", tinyStyle);
        tinyStyle.alignment = TextAnchor.MiddleLeft;
    }

    // ===== HUD の部品 =====

    [Header("HUD の見た目")]
    [Tooltip("数字やラベル用の等幅フォント(IBM Plex Mono など)。空なら HUD Font を使う")]
    public Font monoFont;

    private GUIStyle hudName, hudMono, hudNum;

    void EnsureHudStyles()
    {
        if (hudName != null) return;
        hudName = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow };
        hudNum = new GUIStyle(hudName) { alignment = TextAnchor.MiddleRight };
        hudMono = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow };
        if (hudFont != null) { hudName.font = hudFont; hudNum.font = hudFont; }
        Font mono = monoFont != null ? monoFont : hudFont;
        if (mono != null) hudMono.font = mono;
        hudName.padding = hudNum.padding = hudMono.padding = new RectOffset(0, 0, 0, 0);
    }

    static readonly Color HudPanel = new Color32(0x0D, 0x11, 0x17, 0xF0);
    static readonly Color HudLine = new Color32(0x1E, 0x27, 0x33, 0xFF);
    static readonly Color HudSlot = new Color32(0x0A, 0x0E, 0x14, 0xFF);
    static readonly Color HudLabel = new Color32(0x7D, 0x8A, 0x9B, 0xFF);
    static readonly Color HudGrey = new Color32(0x8B, 0x98, 0xA8, 0xFF);
    static readonly Color HudEmpty = new Color32(0x3A, 0x44, 0x52, 0xFF);
    static readonly Color HudHot = new Color32(0xFF, 0x8A, 0x3D, 0xFF);
    static readonly Color HudRed = new Color32(0xFF, 0x5A, 0x5A, 0xFF);
    static readonly Color HudWhite = new Color32(0xE6, 0xED, 0xF3, 0xFF);

    // 上の真ん中のタイマー
    void DrawTimer(float k, Color amber, Color white)
    {
        EnsureHudStyles();
        int m = Mathf.FloorToInt(timeLeft / 60f);
        int sec = Mathf.FloorToInt(timeLeft % 60f);
        Rect r = new Rect(Screen.width / 2f - 60 * k, 18 * k, 120 * k, 40 * k);
        Fill(r, HudPanel);
        Border(r, HudLine, 1f);
        hudMono.fontSize = Mathf.RoundToInt(22 * k);
        hudMono.alignment = TextAnchor.MiddleCenter;
        hudMono.normal.textColor = (state == State.Playing && timeLeft <= 30f) ? amber : white;
        GUI.Label(r, m + ":" + sec.ToString("00"), hudMono);
        hudMono.alignment = TextAnchor.MiddleLeft;
    }

    // 下の左右のパネル(名前・ストック・HP・武器と熱・部位)
    void DrawHudPanel(bool rightSide, float k, string badge, string name, string sub,
                      int stocks, HealthSystem fighter, bool waiting, WeaponSystem weapon,
                      Color accent, Color badgeText)
    {
        EnsureHudStyles();

        int n = weapon != null ? weapon.WeaponCount : 0;
        int rows = Mathf.Max(1, Mathf.CeilToInt(n / 3f));
        float W = 420f, H = 62f + rows * 44f - 6f;     // 武器3つで1段、6つで2段
        float x0 = rightSide ? Screen.width / k - W - 20f : 20f;
        float y0 = Screen.height / k - H - 14f;

        // パネル
        Rect panel = R(k, x0, y0, W, H);
        Fill(panel, HudPanel);
        Border(panel, HudLine, 1f);

        // バッジ(P1 / P2)
        Rect b = R(k, x0 + 14, y0 + 12, 30, 22);
        Fill(b, accent);
        hudMono.fontSize = Mathf.RoundToInt(12 * k);
        hudMono.alignment = TextAnchor.MiddleCenter;
        hudMono.normal.textColor = badgeText;
        GUI.Label(b, badge, hudMono);
        hudMono.alignment = TextAnchor.MiddleLeft;

        // 名前(と CPU の強さ)
        hudName.fontSize = Mathf.RoundToInt(16 * k);
        hudName.normal.textColor = HudWhite;
        Rect nr = R(k, x0 + 54, y0 + 12, 100, 22);
        GUI.Label(nr, name, hudName);
        if (sub.Length > 0)
        {
            float nw = hudName.CalcSize(new GUIContent(name)).x / k;
            hudMono.fontSize = Mathf.RoundToInt(10 * k);
            hudMono.normal.textColor = HudLabel;
            GUI.Label(R(k, x0 + 54 + nw + 8, y0 + 13, 70, 22), sub, hudMono);
        }

        // ストック(四角)
        for (int i = 0; i < stockCount; i++)
        {
            Rect sq = R(k, x0 + 178 + i * 16, y0 + 18, 10, 10);
            if (i < stocks) Fill(sq, accent);
            else Border(sq, HudEmpty, 1f);
        }

        // HP
        int hp = (fighter != null && !waiting) ? fighter.currentHealth : 0;
        int maxHp = fighter != null ? Mathf.Max(1, fighter.maxHealth) : 100;
        bool low = hp <= maxHp * 0.3f;
        hudMono.fontSize = Mathf.RoundToInt(11 * k);
        hudMono.normal.textColor = HudLabel;
        hudMono.alignment = TextAnchor.MiddleRight;
        GUI.Label(R(k, x0 + W - 110, y0 + 12, 48, 24), "HP", hudMono);
        hudMono.alignment = TextAnchor.MiddleLeft;
        hudNum.fontSize = Mathf.RoundToInt(26 * k);
        hudNum.normal.textColor = low ? HudHot : HudWhite;
        GUI.Label(R(k, x0 + W - 100, y0 + 6, 86, 34), hp.ToString(), hudNum);

        Fill(R(k, x0 + 14, y0 + 42, W - 28, 4), HudLine);
        Fill(R(k, x0 + 14, y0 + 42, (W - 28) * Mathf.Clamp01((float)hp / maxHp), 4), low ? HudHot : accent);

        // 武器スロット(3つずつ並べる)
        for (int i = 0; i < n; i++)
        {
            float sx = x0 + 14 + (i % 3) * 112, sy = y0 + 54 + (i / 3) * 44;
            DrawWeaponSlot(k, weapon, i, sx, sy, 104, 38, accent);
        }

        // 部位(H 頭 / A 腕 / L 脚)
        if (fighter != null && fighter.enablePartDamage)
        {
            string[] labels = { "H", "A", "L" };
            float[] ratios = { fighter.HeadRatio, fighter.ArmRatio, fighter.LegRatio };
            float px = x0 + 352;
            for (int i = 0; i < 3; i++)
            {
                float py = y0 + 60 + i * 12;
                float ratio = waiting ? 1f : Mathf.Clamp01(ratios[i]);
                bool broken = ratio <= 0f;
                hudMono.fontSize = Mathf.RoundToInt(9 * k);
                hudMono.normal.textColor = broken ? HudRed : HudLabel;
                GUI.Label(R(k, px, py - 4, 12, 12), labels[i], hudMono);
                Rect bar = R(k, px + 12, py, 42, 4);
                Fill(bar, HudLine);
                Fill(R(k, px + 12, py, 42 * ratio, 4), ratio > 0.5f ? HudGrey : HudHot);
                if (broken) Border(R(k, px + 11, py - 1, 44, 6), HudRed, 1f);
            }
        }
    }

    // 武器スロット1つ(アイコン・名前・熱ゲージ)
    void DrawWeaponSlot(float k, WeaponSystem weapon, int i, float sx, float sy, float sw, float sh, Color accent)
    {
        bool current = weapon.CurrentIndex == i;
        bool switching = current && weapon.IsSwitching;
        bool locked = weapon.IsLocked(i);
        float heat = weapon.GetHeat(i);
        string wn = weapon.GetWeaponName(i);

        Rect r = R(k, sx, sy, sw, sh);
        Fill(r, HudSlot);
        Border(r, current && !switching ? accent : HudLine, 1f);

        // 持ち替え中:下に進み具合のバー
        if (switching) Fill(R(k, sx, sy + sh - 2, sw * weapon.SwitchProgress, 2), accent);

        // アイコン(弾の飛び方の線)
        Color ic = locked ? HudRed : (current ? accent : HudGrey);
        DrawWeaponIcon(k, wn, sx + 5, sy + 11, ic);

        // 名前
        hudMono.fontSize = Mathf.RoundToInt(9.5f * k);
        hudMono.normal.textColor = locked ? HudRed : HudWhite;
        string label = locked ? "OVERHEAT " + Mathf.CeilToInt(weapon.GetLockRemaining(i)) : (i + 1) + " " + wn;
        GUI.Label(R(k, sx + 42, sy + 4, sw - 44, 16), label, hudMono);

        // 熱ゲージ(オーバーヒート中は残り時間)
        Fill(R(k, sx + 42, sy + 25, sw - 50, 4), HudLine);
        float ratio = locked
            ? Mathf.Clamp01(weapon.GetLockRemaining(i) / Mathf.Max(0.01f, weapon.overheatLockTime))
            : Mathf.Clamp01(heat / 100f);
        Color hc = locked ? HudRed : (heat >= 70f ? HudHot : accent);
        Fill(R(k, sx + 42, sy + 25, (sw - 50) * ratio, 4), hc);
    }

    // 武器ごとのアイコン(64x32 の線を半分の大きさで描く)
    void DrawWeaponIcon(float k, string weaponName, float x, float y, Color c)
    {
        float s = 0.5f, t = 2.5f;
        Vector2 P(float px, float py) => new Vector2((x + px * s) * k, (y + py * s) * k);
        void Line(float ax, float ay, float bx, float by) => DrawLineGUI(P(ax, ay), P(bx, by), t * k, c);
        void Quad(float ax, float ay, float cx, float cy, float bx, float by)
        {
            Vector2 prev = P(ax, ay);
            for (int j = 1; j <= 10; j++)
            {
                float u = j / 10f;
                float qx = (1 - u) * (1 - u) * ax + 2 * (1 - u) * u * cx + u * u * bx;
                float qy = (1 - u) * (1 - u) * ay + 2 * (1 - u) * u * cy + u * u * by;
                Vector2 q = P(qx, qy);
                DrawLineGUI(prev, q, t * k, c);
                prev = q;
            }
        }

        switch ((weaponName ?? "").ToUpper())
        {
            case "BOUND": Line(6, 6, 22, 26); Line(22, 26, 38, 8); Line(38, 8, 58, 20); break;
            case "TRACKING": Quad(6, 26, 30, 26, 38, 16); Quad(38, 16, 46, 6, 58, 6); break;
            case "GRENADE": Quad(6, 28, 32, -6, 58, 28); break;
            case "SHOTGUN": Line(6, 16, 58, 4); Line(6, 16, 58, 16); Line(6, 16, 58, 28); break;
            case "ROUTE": Line(6, 26, 18, 8); Line(18, 8, 32, 24); Line(32, 24, 44, 6); Line(44, 6, 58, 18); break;
            default: Line(6, 16, 58, 16); break; // NORMAL など
        }
    }

    // 太さのある線(画面の座標で)
    void DrawLineGUI(Vector2 a, Vector2 b, float thick, Color c)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return;
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
        Fill(new Rect(a.x - thick / 2f, a.y - thick / 2f, len + thick, thick), c);
        GUI.matrix = m;
    }

    // デザイン上の座標(1280x720)を、実際の画面の座標に変える
    static Rect R(float k, float x, float y, float w, float h)
    {
        return new Rect(x * k, y * k, w * k, h * k);
    }

    void DrawMarker(HealthSystem fighter, bool waiting, string label, Color accent, Color textColor, float unit)
    {
        if (fighter == null || waiting) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(fighter.transform.position + Vector3.up * markerHeight);
        if (sp.z < 0f) return;
        Vector2 p = new Vector2(sp.x, Screen.height - sp.y); // GUI は上が 0

        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = textColor;
        float w = unit * (label.Length > 2 ? 1.5f : 1.1f), h = unit * 0.6f;
        float edge = unit * 0.8f;

        // 画面の中:頭の上にタグと ▼
        bool inside = p.x > edge && p.x < Screen.width - edge && p.y > edge && p.y < Screen.height - edge;
        if (inside)
        {
            float bob = Mathf.Sin(Time.time * 4f) * unit * 0.06f;
            Rect tag = new Rect(p.x - w / 2f, p.y - h - unit * 0.35f + bob, w, h);
            Fill(tag, accent);
            GUI.Label(tag, label, tinyStyle);
            float tri = unit * 0.32f;
            for (int i = 0; i < 4; i++)
            {
                float tw = tri * (1f - i / 4f);
                Fill(new Rect(p.x - tw / 2f, tag.yMax + i * tri / 4f + bob, tw, tri / 4f + 0.5f), accent);
            }
            tinyStyle.alignment = TextAnchor.MiddleLeft;
            return;
        }

        // 画面の外:ふちに寄せて、その方向へ矢印
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
        Vector2 dir = (p - center).normalized;
        Vector2 q = new Vector2(Mathf.Clamp(p.x, edge, Screen.width - edge), Mathf.Clamp(p.y, edge, Screen.height - edge));

        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 8f);
        Rect box = new Rect(q.x - w / 2f, q.y - h / 2f, w, h);
        box.x = Mathf.Clamp(box.x, unit * 0.2f, Screen.width - w - unit * 0.2f);
        box.y = Mathf.Clamp(box.y, unit * 0.2f, Screen.height - h - unit * 0.2f);
        Fill(box, new Color(accent.r, accent.g, accent.b, pulse));
        GUI.Label(box, label, tinyStyle);

        // 矢印(タグの外側、キャラのいる方向)
        Matrix4x4 m = GUI.matrix;
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Vector2 tip = box.center + dir * (Mathf.Max(w, h) * 0.5f + unit * 0.35f);
        GUIUtility.RotateAroundPivot(ang, tip);
        float a = unit * 0.36f;
        for (int i = 0; i < 4; i++)
        {
            float th = a * (1f - i / 4f);
            Fill(new Rect(tip.x - a + i * a / 4f, tip.y - th / 2f, a / 4f + 0.5f, th), accent);
        }
        GUI.matrix = m;
        tinyStyle.alignment = TextAnchor.MiddleLeft;
    }

    void DrawPopups(float unit)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        smallStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.alignment = TextAnchor.MiddleCenter;

        for (int i = popups.Count - 1; i >= 0; i--)
        {
            Popup p = popups[i];
            float age = Time.time - p.startTime;
            if (p.who == null || age > PopupDuration)
            {
                popups.RemoveAt(i);
                continue;
            }

            float u = age / PopupDuration;
            float height = p.small ? 1.2f : 1.6f;
            Vector3 screen = cam.WorldToScreenPoint(p.who.position + new Vector3(p.offsetX, height, 0f));
            if (screen.z < 0f) continue;

            float sx = screen.x;
            float sy = Screen.height - screen.y - u * unit * 1.2f; // 上に浮かぶ
            Rect r = new Rect(sx - unit * 5f, sy - unit * 0.5f, unit * 10f, unit);
            GUIStyle style = p.small ? tinyStyle : smallStyle;

            // 読みやすいように、暗い影を少しずらして重ねる
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f * (1f - u * u));
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), p.text, style);
            style.normal.textColor = new Color(p.color.r, p.color.g, p.color.b, 1f - u * u);
            GUI.Label(r, p.text, style);
        }

        smallStyle.alignment = TextAnchor.MiddleLeft;
        tinyStyle.alignment = TextAnchor.MiddleLeft;
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