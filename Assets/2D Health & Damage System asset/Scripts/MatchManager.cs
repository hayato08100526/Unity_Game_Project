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

    [Header("アイテム")]
    [Tooltip("オン:試合中に REPAIR / COOLANT が出る")]
    public bool spawnItems = true;

    [Header("頭の上のマーカー")]
    [Tooltip("ロボットの足元(中心)から、マーカーを出す高さ")]
    public float markerHeight = 1.6f;
    public bool showMarkers = true;

    [Header("場外ライン(ここを越えたらKO)")]
    public float blastLeft = -16f;
    public float blastRight = 16f;
    public float blastBottom = -8f;
    public float blastTop = 16f;

    private enum State { SelectDifficulty, SelectStage, Countdown, Playing, Result }
    private State state = State.SelectDifficulty;

    // ポーズ中か(Esc で切り替え)
    private bool paused = false;

    // カウントダウンが終わって試合中(ポーズ中でない)なら true。CPU と銃はこれが true のときだけ撃てる
    public bool IsFighting => state == State.Playing && !paused;

    // ===== 戦績(リザルト画面に出す) [0] = 自分, [1] = CPU =====
    private readonly int[] damageTaken = new int[2];
    private readonly int[] partsLost = new int[2];
    private readonly int[] lastHp = { -1, -1 };
    private float fightTime;

    // シーンを読み込み直しても覚えておく(再戦用)
    private static int savedDifficulty = -1;
    private static bool skipSelect = false;

    // ステージ(0 = FACTORY 今までのステージ / 1 = SPLIT 真ん中に穴)
    private static int savedStage = 0;
    private static readonly string[] stageNames = { "FACTORY", "SPLIT", "SHIFT" };
    private static readonly string[] stageNotes = { "The classic arena", "Mind the gap in the middle", "Ride the moving platforms" };
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
        SoundManager.Play("break");
        if (who == player) partsLost[0]++;
        else if (who == cpu) partsLost[1]++;
    }

    // HP が減った分を「受けたダメージ」として数える(復活で増えたときは数えない)
    void TrackDamage(HealthSystem fighter, int i)
    {
        if (fighter == null) return;
        int hp = fighter.currentHealth;
        if (lastHp[i] >= 0 && hp < lastHp[i]) damageTaken[i] += lastHp[i] - hp;
        lastHp[i] = hp;
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

        // 対戦の BGM
        SoundManager.PlayMusic("bgm_battle");

        // アイテムを出す係(REPAIR / COOLANT)
        if (spawnItems && GetComponent<ItemSpawner>() == null) gameObject.AddComponent<ItemSpawner>();

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
            ApplyDifficultyOnly(savedDifficulty);
            ChooseStage(savedStage);
        }
        else
        {
            state = State.SelectDifficulty;
        }
    }

    void ChooseDifficulty(int index)
    {
        ApplyDifficultyOnly(index);
        SoundManager.Play("ui");
        state = State.SelectStage;   // 次はステージ選び
        menuIndex = savedStage;
    }

    // ===== 選ぶ画面のカーソル(← → / LB RB で動かして、Enter / A で決定) =====
    private int menuIndex = 1;

    bool MenuMove(int count)
    {
        int d = 0;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.JoystickButton4)) d = -1;
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.JoystickButton5)) d = 1;
        if (d == 0) return false;
        menuIndex = (menuIndex + d + count) % count;
        SoundManager.Play("ui");
        return true;
    }

    bool MenuConfirm()
    {
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0);
    }

    void ApplyDifficultyOnly(int index)
    {
        savedDifficulty = index;
        var brain = cpu != null ? cpu.GetComponent<CpuController>() : null;
        if (brain != null) brain.ApplyDifficulty((CpuController.Difficulty)index);
    }

    void ChooseStage(int index)
    {
        savedStage = index;
        ApplyStage(index);

        // ステージに合わせて背景の雰囲気を変える(BackgroundFX があるとき)
        var fx = FindFirstObjectByType<BackgroundFX>();
        if (fx != null) fx.SetMood(index);

        state = State.Countdown;
        countdownLeft = countdownTime;
        SoundManager.Play("count");   // 最初の「3」
    }

    // ===== ステージの並べ替え =====
    // シーンの足場(MainFloor / Platform_LowL など)を動かして、別のステージにする
    void ApplyStage(int index)
    {
        var brain = cpu != null ? cpu.GetComponent<CpuController>() : null;
        if (index == 2)
        {
            ApplyShiftStage(brain);
            return;
        }
        if (index != 1)
        {
            if (brain != null) brain.SetGap(false, 0f, 0f);
            return; // FACTORY はシーンのまま
        }

        // SPLIT:床を左右の島に分けて、真ん中に幅 4 の穴
        GameObject floor = GameObject.Find("MainFloor");
        if (floor != null)
        {
            floor.transform.position = new Vector3(-5.5f, -0.5f, floor.transform.position.z);
            floor.transform.localScale = new Vector3(7f, floor.transform.localScale.y, 1f);
            GameObject right = Instantiate(floor, new Vector3(5.5f, -0.5f, floor.transform.position.z), floor.transform.rotation);
            right.name = "MainFloor_R";
        }

        // 足場:穴の上の橋、左右の中段、高いところ
        PlacePlatform("Platform_MidL", 0f, 1.6f, 3f);     // 穴の上の橋
        PlacePlatform("Platform_LowL", -5.5f, 3.0f, 3f);
        PlacePlatform("Platform_LowR", 5.5f, 3.0f, 3f);
        PlacePlatform("Platform_MidR", 0f, 4.6f, 4f);
        GameObject top = PlacePlatform("Platform_Top", -7.5f, 5.8f, 2.4f);
        if (top != null)
        {
            GameObject top2 = Instantiate(top, new Vector3(7.5f, 5.8f, top.transform.position.z), top.transform.rotation);
            top2.name = "Platform_Top_R";
        }

        // CPU に穴の場所を教える(落ちないように飛び越える)
        if (brain != null) brain.SetGap(true, -2f, 2f);
    }

    // SHIFT:真ん中だけの床 + 左右に動く足場 + 真ん中のエレベーター
    void ApplyShiftStage(CpuController brain)
    {
        GameObject floor = GameObject.Find("MainFloor");
        if (floor != null)
        {
            floor.transform.position = new Vector3(0f, -0.5f, floor.transform.position.z);
            floor.transform.localScale = new Vector3(12f, floor.transform.localScale.y, 1f);
        }

        MakeMover(PlacePlatform("Platform_LowL", -8.5f, 2.0f, 3f), new Vector2(2.2f, 0f), 5f, 0f);
        MakeMover(PlacePlatform("Platform_LowR", 8.5f, 2.0f, 3f), new Vector2(2.2f, 0f), 5f, 0.5f);
        MakeMover(PlacePlatform("Platform_MidL", 0f, 3.2f, 3f), new Vector2(0f, 1.4f), 4f, 0.25f);
        PlacePlatform("Platform_MidR", -4.5f, 5.4f, 2.6f);
        PlacePlatform("Platform_Top", 4.5f, 5.4f, 2.6f);

        // CPU に床の端を教える(床は -6〜6)
        if (brain != null)
        {
            brain.SetGap(false, 0f, 0f);
            brain.stageLeft = -6f;
            brain.stageRight = 6f;
        }
    }

    void MakeMover(GameObject p, Vector2 offset, float period, float phase)
    {
        if (p == null) return;
        p.name = "Mover_" + p.name;   // アイテムが動く足場の上に出ないように名前を変える
        var mp = p.GetComponent<MovingPlatform>();
        if (mp == null) mp = p.AddComponent<MovingPlatform>();
        mp.offset = offset;
        mp.period = period;
        mp.phase = phase;
    }

    GameObject PlacePlatform(string objectName, float x, float y, float width)
    {
        GameObject p = GameObject.Find(objectName);
        if (p == null) return null;
        p.transform.position = new Vector3(x, y, p.transform.position.z);
        p.transform.localScale = new Vector3(width, p.transform.localScale.y, 1f);
        return p;
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
            // ポーズ:ESC / P / ゲームパッドの START
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.JoystickButton7))
            {
                if (paused) Resume(); else Pause();
                return;
            }
            if (paused)
            {
                if (Input.GetKeyDown(KeyCode.JoystickButton0)) Resume();                                  // A
                else if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton3)) Reload(true); // Y
                else if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.JoystickButton1)) GoToTitle();  // B
                return; // ポーズ中は試合を進めない
            }
        }

        switch (state)
        {
            case State.SelectDifficulty:
                if (Input.GetKeyDown(KeyCode.Alpha1)) ChooseDifficulty(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) ChooseDifficulty(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) ChooseDifficulty(2);
                else if (MenuMove(3)) { }
                else if (MenuConfirm()) ChooseDifficulty(menuIndex);
                break;

            case State.SelectStage:
                if (Input.GetKeyDown(KeyCode.Alpha1)) ChooseStage(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) ChooseStage(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) ChooseStage(2);
                else if (MenuMove(stageNames.Length)) { }
                else if (MenuConfirm()) ChooseStage(menuIndex);
                else if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.JoystickButton1)) { state = State.SelectDifficulty; menuIndex = 1; } // 強さ選びに戻る
                break;

            case State.Countdown:
                {
                    int before = Mathf.CeilToInt(countdownLeft);
                    countdownLeft -= Time.deltaTime;
                    int after = Mathf.CeilToInt(countdownLeft);
                    if (after != before && after > 0) SoundManager.Play("count");
                }
                if (countdownLeft <= 0f)
                {
                    SoundManager.Play("fight");
                    state = State.Playing;
                    fightTextTimer = 0.8f;
                    SetControl(player, true);
                    SetControl(cpu, true);
                }
                break;

            case State.Playing:
                if (fightTextTimer > 0f) fightTextTimer -= Time.deltaTime;
                fightTime += Time.deltaTime;
                TrackDamage(player, 0);
                TrackDamage(cpu, 1);

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
                if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton0)) Reload(true);   // 同じ強さで再戦(A)
                else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.JoystickButton3)) Reload(false); // 強さを選び直す(Y)
                else if (Input.GetKeyDown(KeyCode.JoystickButton1)) GoToTitle();                              // タイトル(B)
                else if (Input.GetKeyDown(KeyCode.T)) GoToTitle();
                break;
        }
    }

    void Pause()
    {
        SoundManager.Play("ui");
        SoundManager.DuckMusic(0.4f);
        paused = true;
        Time.timeScale = 0f;
        SetControl(player, false);
        SetControl(cpu, false);
    }

    void Resume()
    {
        SoundManager.Play("ui");
        SoundManager.DuckMusic(1f);
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

        // KO の破片(場外なら画面の中に見えるように、ステージ側へ寄せる)
        Vector3 kp = fighter.transform.position;
        Vector2 burstPos = new Vector2(Mathf.Clamp(kp.x, blastLeft + 2f, blastRight - 2f), Mathf.Clamp(kp.y, blastBottom + 2f, blastTop - 2f));
        HitEffect.Burst(burstPos, isPlayer ? new Color32(0x4F, 0xE3, 0xF0, 0xFF) : new Color32(0xFF, 0xB5, 0x47, 0xFF));

        fighter.KnockOut();
        SoundManager.Play("ko");
        // 場外 KO で HP が 0 にされた分は、ダメージに数えない
        lastHp[isPlayer ? 0 : 1] = fighter.currentHealth;

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
        SoundManager.Play(text == "YOU WIN" ? "win" : "lose");
        SoundManager.DuckMusic(0.35f);   // リザルト中は BGM を小さく
        state = State.Result;
        resultText = text;
        SetControl(player, false);
        SetControl(cpu, false);

        // 戦績に記録(新記録ならリザルトに出す)
        recordNews = Records.Report(savedDifficulty, text, fightTime);
    }

    private string recordNews = "";

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
        if (showMarkers && state != State.SelectDifficulty && state != State.SelectStage)
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
        else if (state == State.SelectStage)
        {
            DrawStageSelect(unit, cyan, white);
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
            DrawResult(unit, cyan, amber, white);
        }

        // ポーズ画面(一番上に重ねる)
        if (paused) DrawPauseMenu(unit, cyan, white);
    }

    // ===== リザルト画面(勝敗・戦績・ボタン) =====
    void DrawResult(float unit, Color cyan, Color amber, Color white)
    {
        TrackDamage(player, 0);   // 最後の一撃も数える
        TrackDamage(cpu, 1);

        Color card = new Color32(0x0D, 0x11, 0x17, 0xF2);
        Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);
        Color grey = new Color32(0x8B, 0x98, 0xA8, 0xFF);

        // 画面を暗くする
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color32(0x07, 0x09, 0x0D, 0xB4));

        // 勝敗
        Color resultColor = resultText == "YOU WIN" ? cyan : (resultText == "YOU LOSE" ? amber : white);
        bigStyle.normal.textColor = resultColor;
        GUI.Label(new Rect(0, Screen.height * 0.08f, Screen.width, unit * 3f), resultText, bigStyle);

        // 新記録(点滅)
        if (!string.IsNullOrEmpty(recordNews))
        {
            bool on = Mathf.FloorToInt(Time.unscaledTime * 3f) % 2 == 0;
            tinyStyle.alignment = TextAnchor.MiddleCenter;
            tinyStyle.normal.textColor = on ? amber : white;
            GUI.Label(new Rect(0, Screen.height * 0.08f + unit * 2.55f, Screen.width, unit * 0.8f), recordNews, tinyStyle);
            tinyStyle.alignment = TextAnchor.MiddleLeft;
        }

        // 戦績カード
        float w = unit * 16f, rowH = unit * 1.05f;
        string[] labels = { "DAMAGE DEALT", "KO", "PARTS BROKEN", "STOCKS LEFT" };
        int[] you = { damageTaken[1], stockCount - cpuStocks, partsLost[1], playerStocks };
        int[] them = { damageTaken[0], stockCount - playerStocks, partsLost[0], cpuStocks };

        float x = (Screen.width - w) / 2f;
        float y = Screen.height * 0.08f + unit * 3.4f;
        float h = rowH * (labels.Length + 1) + unit * 1.4f;
        Rect cardRect = new Rect(x, y, w, h);
        Fill(cardRect, card);
        Border(cardRect, line, 1f);
        Fill(new Rect(x, y, w, unit * 0.1f), resultColor);

        float colL = x + w * 0.42f, colR = x + w * 0.74f, colW = w * 0.22f;
        float ty = y + unit * 0.5f;

        // 見出し(YOU / CPU)
        smallStyle.alignment = TextAnchor.MiddleCenter;
        smallStyle.normal.textColor = cyan;
        GUI.Label(new Rect(colL, ty, colW, rowH), "YOU", smallStyle);
        smallStyle.normal.textColor = amber;
        GUI.Label(new Rect(colR, ty, colW, rowH), "CPU", smallStyle);
        ty += rowH;

        for (int i = 0; i < labels.Length; i++)
        {
            Fill(new Rect(x + unit * 0.6f, ty, w - unit * 1.2f, 1f), line);
            tinyStyle.alignment = TextAnchor.MiddleLeft;
            tinyStyle.normal.textColor = grey;
            GUI.Label(new Rect(x + unit * 0.8f, ty, w * 0.4f, rowH), labels[i], tinyStyle);

            // 多い方を明るく(STOCKS LEFT 以外も「多い方が良い」項目)
            bool youBetter = you[i] > them[i], themBetter = them[i] > you[i];
            smallStyle.normal.textColor = youBetter ? white : grey;
            GUI.Label(new Rect(colL, ty, colW, rowH), you[i].ToString(), smallStyle);
            smallStyle.normal.textColor = themBetter ? white : grey;
            GUI.Label(new Rect(colR, ty, colW, rowH), them[i].ToString(), smallStyle);
            ty += rowH;
        }
        smallStyle.alignment = TextAnchor.MiddleLeft;

        // 試合時間と強さ
        int m = Mathf.FloorToInt(fightTime / 60f), sec = Mathf.FloorToInt(fightTime % 60f);
        string level = savedDifficulty >= 0 ? difficultyNames[savedDifficulty] : "";
        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = grey;
        GUI.Label(new Rect(x, ty + unit * 0.15f, w, unit * 0.7f), "TIME  " + m + ":" + sec.ToString("00") + "      CPU  " + level, tinyStyle);
        tinyStyle.alignment = TextAnchor.MiddleLeft;

        // ボタン(クリックでも、R / D / T キーでも)
        string[] btn = { "REMATCH  [R]", "CHANGE LEVEL  [D]", "TITLE  [T]" };
        float bw = unit * 6.2f, bh = unit * 1.4f, gap = unit * 0.4f;
        float bx = (Screen.width - (bw * 3 + gap * 2)) / 2f;
        float by = y + h + unit * 0.8f;
        for (int i = 0; i < 3; i++)
        {
            Rect r = new Rect(bx + i * (bw + gap), by, bw, bh);
            bool hover = r.Contains(Event.current.mousePosition);
            bool primary = i == 0;
            Fill(r, primary ? (hover ? white : cyan) : card);
            if (!primary) Border(r, hover ? cyan : line, hover ? 2f : 1f);
            tinyStyle.alignment = TextAnchor.MiddleCenter;
            tinyStyle.normal.textColor = primary ? (Color)new Color32(0x03, 0x14, 0x17, 0xFF) : (hover ? cyan : white);
            GUI.Label(r, btn[i], tinyStyle);
            tinyStyle.alignment = TextAnchor.MiddleLeft;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (i == 0) Reload(true);
                else if (i == 1) Reload(false);
                else GoToTitle();
            }
        }
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

    // ステージ選び(カードの中に足場の形を小さく描く)
    void DrawStageSelect(float unit, Color accent, Color white)
    {
        Color panel = new Color32(0x07, 0x09, 0x0D, 0xE6);
        Color card = new Color32(0x0D, 0x11, 0x17, 0xFF);
        Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);
        Color plat = new Color32(0x3A, 0x44, 0x52, 0xFF);

        Fill(new Rect(0, 0, Screen.width, Screen.height), panel);

        midStyle.normal.textColor = white;
        GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, unit * 1.4f), "SELECT STAGE", midStyle);

        int count = stageNames.Length;
        float gap = unit * 0.7f;
        float w = Mathf.Min(unit * 9f, (Screen.width - unit * 2f - gap * (count - 1)) / count), h = unit * 7f;
        float x0 = (Screen.width - (w * count + gap * (count - 1))) / 2f;
        float y0 = Screen.height * 0.25f;

        // 足場の配置(ステージの座標:x -10〜10、y -1〜7)
        float[][][] layouts =
        {
            new[] { new[]{0f,-0.5f,20f}, new[]{-6.5f,2.2f,3f}, new[]{6.5f,2.2f,3f}, new[]{-3f,4.2f,3f}, new[]{3f,4.2f,3f}, new[]{0f,6.2f,3f} },
            new[] { new[]{-5.5f,-0.5f,7f}, new[]{5.5f,-0.5f,7f}, new[]{0f,1.6f,3f}, new[]{-5.5f,3f,3f}, new[]{5.5f,3f,3f}, new[]{0f,4.6f,4f}, new[]{-7.5f,5.8f,2.4f}, new[]{7.5f,5.8f,2.4f} },
            new[] { new[]{0f,-0.5f,12f}, new[]{-8.5f,2f,3f}, new[]{8.5f,2f,3f}, new[]{0f,3.2f,3f}, new[]{-4.5f,5.4f,2.6f}, new[]{4.5f,5.4f,2.6f} },
        };

        for (int i = 0; i < count; i++)
        {
            Rect r = new Rect(x0 + i * (w + gap), y0, w, h);
            bool hover = r.Contains(Event.current.mousePosition) || menuIndex == i;   // マウスかカーソル
            Fill(r, card);
            Border(r, hover ? accent : line, hover ? 2f : 1f);

            // 小さい地図
            Rect map = new Rect(r.x + unit * 0.5f, r.y + unit * 0.5f, r.width - unit, h * 0.55f);
            Fill(map, new Color32(0x10, 0x16, 0x1E, 0xFF));
            float sx = map.width / 22f, sy = map.height / 9f;
            foreach (float[] p in layouts[i])
            {
                float px = map.x + (p[0] - p[2] / 2f + 11f) * sx;
                float py = map.y + (7.5f - p[1]) * sy;
                float ph = p[2] >= 7f ? sy * 0.9f : Mathf.Max(3f, sy * 0.3f);
                Fill(new Rect(px, py, p[2] * sx, ph), plat);
            }

            // SHIFT:動く足場の向きを矢印で
            if (i == 2)
            {
                Color arrow = accent;
                float ay = map.y + (7.5f - 2f) * sy - sy * 0.6f;
                MiniArrow(new Vector2(map.x + (-8.5f + 11f) * sx, ay), true, sx * 2f, arrow);
                MiniArrow(new Vector2(map.x + (8.5f + 11f) * sx, ay), true, sx * 2f, arrow);
                MiniArrow(new Vector2(map.x + 11f * sx + sx * 2f, map.y + (7.5f - 3.2f) * sy), false, sy * 1.2f, arrow);
            }

            midStyle.normal.textColor = hover ? accent : white;
            GUI.Label(new Rect(r.x, map.yMax + unit * 0.3f, w, unit * 1.1f), (i + 1) + "  " + stageNames[i], midStyle);

            tinyStyle.alignment = TextAnchor.MiddleCenter;
            tinyStyle.normal.textColor = new Color(0.55f, 0.6f, 0.66f);
            GUI.Label(new Rect(r.x, map.yMax + unit * 1.4f, w, unit * 0.6f), stageNotes[i], tinyStyle);
            tinyStyle.alignment = TextAnchor.MiddleLeft;

            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) ChooseStage(i);
        }

        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = new Color(0.55f, 0.6f, 0.66f);
        GUI.Label(new Rect(0, y0 + h + unit * 0.7f, Screen.width, unit), "Click a card or press 1 / 2 / 3      BACKSPACE : change level", tinyStyle);
        tinyStyle.alignment = TextAnchor.MiddleLeft;
    }

    // 小さい両矢印(horizontal = 横向き、false なら縦向き)
    void MiniArrow(Vector2 c, bool horizontal, float len, Color col)
    {
        float t = 2f, hd = Mathf.Max(3f, len * 0.25f);
        if (horizontal)
        {
            Fill(new Rect(c.x - len / 2f, c.y - t / 2f, len, t), col);
            for (int k = 0; k < 3; k++)
            {
                float s = hd * (1f - k / 3f);
                Fill(new Rect(c.x - len / 2f + k * 1.5f, c.y - s / 2f, 1.5f, s), col);
                Fill(new Rect(c.x + len / 2f - (k + 1) * 1.5f, c.y - s / 2f, 1.5f, s), col);
            }
        }
        else
        {
            Fill(new Rect(c.x - t / 2f, c.y - len / 2f, t, len), col);
            for (int k = 0; k < 3; k++)
            {
                float s = hd * (1f - k / 3f);
                Fill(new Rect(c.x - s / 2f, c.y - len / 2f + k * 1.5f, s, 1.5f), col);
                Fill(new Rect(c.x - s / 2f, c.y + len / 2f - (k + 1) * 1.5f, s, 1.5f), col);
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
            bool hover = r.Contains(Event.current.mousePosition) || menuIndex == i;   // マウスかカーソル

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