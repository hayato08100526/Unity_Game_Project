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

    [Header("場外ライン(ここを越えたらKO)")]
    public float blastLeft = -16f;
    public float blastRight = 16f;
    public float blastBottom = -8f;
    public float blastTop = 16f;

    private enum State { Countdown, Playing, Result }
    private State state = State.Countdown;

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

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        Time.timeScale = 1f;

        if (player == null) player = FindFighter("Player");
        if (cpu == null) cpu = FindFighter("CPU");

        playerWeapon = FindWeapon(player);
        cpuWeapon = FindWeapon(cpu);

        playerStocks = stockCount;
        cpuStocks = stockCount;
        timeLeft = matchTime;
        countdownLeft = countdownTime;

        SetControl(player, false);
        SetControl(cpu, false);
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
                if (Input.GetKeyDown(KeyCode.R))
                {
                    Time.timeScale = 1f;
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                }
                break;
        }
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
        }

        float unit = Screen.height / 20f;
        bigStyle.fontSize = Mathf.RoundToInt(unit * 2.4f);
        midStyle.fontSize = Mathf.RoundToInt(unit * 1.0f);
        smallStyle.fontSize = Mathf.RoundToInt(unit * 0.7f);
        tinyStyle.fontSize = Mathf.RoundToInt(unit * 0.42f);

        Color cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
        Color amber = new Color32(0xFF, 0xB5, 0x47, 0xFF);
        Color white = new Color32(0xE6, 0xED, 0xF3, 0xFF);

        // タイマー
        int m = Mathf.FloorToInt(timeLeft / 60f);
        int s = Mathf.FloorToInt(timeLeft % 60f);
        midStyle.normal.textColor = white;
        GUI.Label(new Rect(0, unit * 0.3f, Screen.width, unit * 1.2f), m + ":" + s.ToString("00"), midStyle);

        // 自分と CPU の情報
        DrawFighterInfo(new Rect(unit * 0.6f, Screen.height - unit * 2.4f, Screen.width * 0.4f, unit * 2f),
                        "YOU", playerStocks, player, playerWaiting, cyan, TextAnchor.UpperLeft);
        DrawFighterInfo(new Rect(Screen.width * 0.6f - unit * 0.6f, Screen.height - unit * 2.4f, Screen.width * 0.4f, unit * 2f),
                        "CPU", cpuStocks, cpu, cpuWaiting, amber, TextAnchor.UpperRight);

        // 自分の武器と熱ゲージ(画面下の中央)
        DrawWeaponSlots(playerWeapon, unit, cyan, white);

        // カウントダウン / FIGHT / 結果
        Rect center = new Rect(0, Screen.height * 0.3f, Screen.width, unit * 3f);
        if (state == State.Countdown)
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
            GUI.Label(new Rect(0, center.yMax, Screen.width, unit * 1.2f), "Press R to rematch", midStyle);
        }
    }

    void DrawFighterInfo(Rect area, string label, int stocks, HealthSystem fighter, bool waiting, Color color, TextAnchor align)
    {
        smallStyle.alignment = align;
        smallStyle.normal.textColor = color;

        string stockMarks = "";
        for (int i = 0; i < stockCount; i++) stockMarks += i < stocks ? "■" : "□";

        int hp = (fighter != null && !waiting) ? fighter.currentHealth : 0;
        string text = label + "  " + stockMarks + "\nHP " + hp;

        // CPU は今持っている武器も表示する
        if (fighter == cpu && cpuWeapon != null)
            text += "\n" + cpuWeapon.GetWeaponName(cpuWeapon.CurrentIndex);

        GUI.Label(area, text, smallStyle);
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