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

    [Header("画面表示")]
    [Tooltip("HUD に使うフォント(Fonts フォルダの Oxanium-Bold など .ttf のもの)。空なら標準のフォント")]
    public Font hudFont;

    [Header("場外ライン(ここを越えたらKO)")]
    public float blastLeft = -16f;
    public float blastRight = 16f;
    public float blastBottom = -8f;
    public float blastTop = 16f;

    private enum State { SelectDifficulty, Countdown, Playing, Result }
    private State state = State.SelectDifficulty;

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
        popups.Add(new Popup { who = who.transform, text = text, startTime = Time.time });
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
                break;
        }
    }

    void Reload(bool sameDifficulty)
    {
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

        // タイマー(残り30秒でアンバーに)
        int m = Mathf.FloorToInt(timeLeft / 60f);
        int s = Mathf.FloorToInt(timeLeft % 60f);
        Rect timerRect = new Rect((Screen.width - unit * 3.4f) / 2f, unit * 0.4f, unit * 3.4f, unit * 1.3f);
        Fill(timerRect, new Color32(0x0D, 0x11, 0x17, 0xE0));
        Border(timerRect, new Color32(0x1E, 0x27, 0x33, 0xFF), 1f);
        midStyle.normal.textColor = (state == State.Playing && timeLeft <= 30f) ? amber : white;
        GUI.Label(timerRect, m + ":" + s.ToString("00"), midStyle);

        // 自分と CPU のパネル
        string cpuSub = "";
        if (savedDifficulty >= 0) cpuSub = difficultyNames[savedDifficulty];
        if (cpuWeapon != null) cpuSub += (cpuSub.Length > 0 ? "  /  " : "") + cpuWeapon.GetWeaponName(cpuWeapon.CurrentIndex);

        DrawFighterPanel(false, unit, "P1", "YOU", "", playerStocks, player, playerWaiting,
                         cyan, new Color32(0x03, 0x14, 0x17, 0xFF), white);
        DrawFighterPanel(true, unit, "CPU", "CPU", cpuSub, cpuStocks, cpu, cpuWaiting,
                         amber, new Color32(0x1F, 0x13, 0x00, 0xFF), white);

        // 自分の武器と熱ゲージ(画面下の中央)
        DrawWeaponSlots(playerWeapon, unit, cyan, white);

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
            GUI.Label(new Rect(0, center.yMax, Screen.width, unit * 1.2f), "R : REMATCH    D : CHANGE LEVEL", midStyle);
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

    // 画面下の左右に出す、1人分のパネル
    void DrawFighterPanel(bool rightSide, float unit, string badge, string name, string sub,
                          int stocks, HealthSystem fighter, bool waiting,
                          Color accent, Color badgeText, Color white)
    {
        Color panel = new Color32(0x0D, 0x11, 0x17, 0xE0);
        Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);
        Color grey = new Color32(0x8B, 0x98, 0xA8, 0xFF);
        Color hot = new Color32(0xFF, 0x8A, 0x3D, 0xFF);

        float w = unit * 7.6f, h = unit * 2.5f;
        float x = rightSide ? Screen.width - w - unit * 0.5f : unit * 0.5f;
        float y = Screen.height - h - unit * 0.5f;
        Rect r = new Rect(x, y, w, h);

        Fill(r, panel);
        Border(r, line, 1f);

        // バッジ(P1 / CPU)
        Rect badgeRect = new Rect(x + unit * 0.3f, y + unit * 0.3f, unit * 1.4f, unit * 0.75f);
        Fill(badgeRect, accent);
        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = badgeText;
        GUI.Label(badgeRect, badge, tinyStyle);

        // 名前と補足(強さ・武器)
        smallStyle.alignment = TextAnchor.MiddleLeft;
        smallStyle.normal.textColor = white;
        GUI.Label(new Rect(x + unit * 2f, y + unit * 0.22f, w * 0.5f, unit * 0.9f), name, smallStyle);

        // 部位の状態(H 頭 / A 腕 / L 脚):無事=枠だけ、半分以下=オレンジ、壊れた=赤
        float px = x + unit * 0.3f, py = y + unit * 1.25f, pw = unit * 0.62f, ph = unit * 0.5f;
        if (fighter != null && fighter.enablePartDamage)
        {
            string[] partLabels = { "H", "A", "L" };
            float[] ratios = { fighter.HeadRatio, fighter.ArmRatio, fighter.LegRatio };
            Color red = new Color32(0xFF, 0x5A, 0x5A, 0xFF);

            for (int i = 0; i < 3; i++)
            {
                Rect pr = new Rect(px + i * (pw + unit * 0.12f), py, pw, ph);
                bool broken = ratios[i] <= 0f;
                bool damaged = ratios[i] <= 0.5f;

                if (broken) Fill(pr, red);
                else if (damaged) Fill(pr, hot);
                else Border(pr, line, 1f);

                tinyStyle.alignment = TextAnchor.MiddleCenter;
                tinyStyle.normal.textColor = (broken || damaged) ? new Color32(0x07, 0x09, 0x0D, 0xFF) : grey;
                GUI.Label(pr, partLabels[i], tinyStyle);
            }
        }

        // 補足(強さ・武器)は部位の右に
        tinyStyle.alignment = TextAnchor.MiddleLeft;
        tinyStyle.normal.textColor = grey;
        if (sub.Length > 0)
            GUI.Label(new Rect(px + 3f * (pw + unit * 0.12f) + unit * 0.15f, py - unit * 0.05f, w * 0.45f, unit * 0.6f), sub, tinyStyle);

        // ストック(右上の四角)
        float sq = unit * 0.42f, sgap = unit * 0.18f;
        for (int i = 0; i < stockCount; i++)
        {
            Rect s = new Rect(x + w - unit * 0.35f - (stockCount - i) * (sq + sgap) + sgap, y + unit * 0.45f, sq, sq);
            if (i < stocks) Fill(s, accent);
            else Border(s, line, 1f);
        }

        // HP の数字(右下に大きく)
        int hp = (fighter != null && !waiting) ? fighter.currentHealth : 0;
        int maxHp = fighter != null ? Mathf.Max(1, fighter.maxHealth) : 100;

        midStyle.alignment = TextAnchor.MiddleRight;
        midStyle.normal.textColor = hp <= maxHp * 0.3f ? hot : white;
        GUI.Label(new Rect(x + w * 0.4f, y + unit * 0.95f, w * 0.6f - unit * 0.35f, unit * 1.0f), hp.ToString(), midStyle);
        midStyle.alignment = TextAnchor.MiddleCenter;

        // HP バー
        Rect bar = new Rect(x + unit * 0.3f, y + h - unit * 0.42f, w - unit * 0.6f, unit * 0.16f);
        Fill(bar, line);
        float ratio = Mathf.Clamp01((float)hp / maxHp);
        Fill(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), hp <= maxHp * 0.3f ? hot : accent);

        tinyStyle.alignment = TextAnchor.MiddleLeft;
    }

    void DrawWeaponSlots(WeaponSystem weapon, float unit, Color accent, Color white)
    {
        if (weapon == null || weapon.WeaponCount == 0) return;

        Color panel = new Color32(0x0D, 0x11, 0x17, 0xE0);
        Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);
        Color hot = new Color32(0xFF, 0x8A, 0x3D, 0xFF);
        Color red = new Color32(0xFF, 0x5A, 0x5A, 0xFF);

        int n = weapon.WeaponCount;
        float slotW = unit * 3.1f;
        float slotH = unit * 1.3f;
        float gap = unit * 0.2f;
        float totalW = n * slotW + (n - 1) * gap;
        float x0 = (Screen.width - totalW) / 2f;
        float y0 = Screen.height - slotH - unit * 0.5f;

        for (int i = 0; i < n; i++)
        {
            Rect r = new Rect(x0 + i * (slotW + gap), y0, slotW, slotH);
            bool current = weapon.CurrentIndex == i;
            bool locked = weapon.IsLocked(i);
            float heat = weapon.GetHeat(i);

            Fill(r, panel);
            Border(r, current ? accent : line, current ? 2f : 1f);

            // 持ち替え中のゲージ(上の線)
            if (current && weapon.IsSwitching)
                Fill(new Rect(r.x, r.y, r.width * weapon.SwitchProgress, unit * 0.12f), accent);

            // 名前
            tinyStyle.normal.textColor = locked ? red : (current ? white : new Color(0.55f, 0.6f, 0.66f));
            string label = locked
                ? (i + 1) + " OVERHEAT " + Mathf.CeilToInt(weapon.GetLockRemaining(i))
                : (i + 1) + " " + weapon.GetWeaponName(i);
            GUI.Label(new Rect(r.x + unit * 0.25f, r.y + unit * 0.1f, r.width - unit * 0.3f, slotH * 0.55f), label, tinyStyle);

            // 熱ゲージ
            Rect bar = new Rect(r.x + unit * 0.25f, r.yMax - unit * 0.38f, r.width - unit * 0.5f, unit * 0.16f);
            Fill(bar, line);
            Color barColor = locked ? red : (heat >= 70f ? hot : accent);
            // オーバーヒート中は、残り時間に合わせてゲージが減っていく
            float ratio = locked
                ? Mathf.Clamp01(weapon.GetLockRemaining(i) / Mathf.Max(0.01f, weapon.overheatLockTime))
                : Mathf.Clamp01(heat / 100f);
            Fill(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), barColor);
        }
    }

    void DrawPopups(float unit)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Color hot = new Color32(0xFF, 0x8A, 0x3D, 0xFF);
        smallStyle.alignment = TextAnchor.MiddleCenter;

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
            Vector3 screen = cam.WorldToScreenPoint(p.who.position + Vector3.up * 1.6f);
            if (screen.z < 0f) continue;

            float sx = screen.x;
            float sy = Screen.height - screen.y - u * unit * 1.2f; // 上に浮かぶ
            Rect r = new Rect(sx - unit * 5f, sy - unit * 0.5f, unit * 10f, unit);

            // 読みやすいように、暗い影を少しずらして重ねる
            smallStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f * (1f - u * u));
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), p.text, smallStyle);
            smallStyle.normal.textColor = new Color(hot.r, hot.g, hot.b, 1f - u * u);
            GUI.Label(r, p.text, smallStyle);
        }

        smallStyle.alignment = TextAnchor.MiddleLeft;
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