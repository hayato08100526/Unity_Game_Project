using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

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
    // (オンラインではポーズしても試合は止まらないので、ポーズ中も true)
    public bool IsFighting => state == State.Playing && (!paused || IsOnlineMode);

    // ===== オンライン対戦 =====
    // 同じオブジェクトに OnlineMatch が付いていれば、オンライン対戦として動く
    private OnlineMatch net;
    public bool IsOnlineMode => net != null;
    private bool IsHostOnline => net != null && net.IsSpawned && net.IsServer;
    private bool fightersFound;
    private bool opponentLeft;
    private int appliedStage = -1;
    private readonly bool[] hostWaiting = new bool[2];

    [Header("オンライン:参加した人(P2)のロボットの見た目")]
    [Tooltip("Robot_CPU を入れる")]
    public Sprite p2BodySprite;
    [Tooltip("Robot_Gun_CPU を入れる")]
    public Sprite p2GunSprite;
    private readonly float[] graceUntil = new float[2];

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
        if (net != null)
        {
            net.OnKo -= OnNetKo;
            net.OnItemSpawned -= OnNetItemSpawned;
            net.OnItemTaken -= OnNetItemTaken;
        }
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

        // オンライン対戦:ロボットはあとから出てくるので、ここでは探さない(Update で探す)
        net = GetComponent<OnlineMatch>();
        if (net != null)
        {
            net.OnKo += OnNetKo;
            net.OnItemSpawned += OnNetItemSpawned;
            net.OnItemTaken += OnNetItemTaken;
            playerStocks = stockCount;
            cpuStocks = stockCount;
            timeLeft = matchTime;
            state = State.SelectStage;
            menuIndex = savedStage;
            return;
        }

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
        // オンライン:ステージを決められるのはホストだけ(決めたら2人の画面に同じステージが出る)
        if (IsOnlineMode)
        {
            if (IsHostOnline && fightersFound && state == State.SelectStage) HostBegin(index);
            return;
        }

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
        if (IsOnlineMode)
        {
            OnlineUpdate();
            return;
        }

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
        if (!IsOnlineMode) Time.timeScale = 0f;   // オンラインでは時間を止めない(相手は動いている)
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
        paused = false;
        Time.timeScale = 1f;

        // オンライン:通信を切ってから戻る(NetworkManager も消して、次にまた最初からつなげるようにする)
        if (IsOnlineMode && NetworkManager.Singleton != null)
        {
            NetworkManager nm = NetworkManager.Singleton;
            nm.Shutdown();
            Destroy(nm.gameObject);
        }
        // タイトルのシーン名がまちがっていても、ビルドの一番上のシーン(タイトル)に戻る
        if (Application.CanStreamedLevelBeLoaded(titleSceneName)) SceneManager.LoadScene(titleSceneName);
        else SceneManager.LoadScene(0);
    }

    void Reload(bool sameDifficulty)
    {
        // オンライン:再戦できるのはホストだけ。2人とも同じシーンを読み込み直す
        if (IsOnlineMode)
        {
            if (!IsHostOnline) return;
            paused = false;
            skipSelect = true;   // 同じステージで、すぐに始める
            NetworkManager.Singleton.SceneManager.LoadScene(SceneManager.GetActiveScene().name, LoadSceneMode.Single);
            return;
        }

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

        // 戦績に記録(新記録ならリザルトに出す)。オンライン対戦は CPU の戦績には入れない
        recordNews = IsOnlineMode ? "" : Records.Report(savedDifficulty, text, fightTime);
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

        // 画面が切り替わった瞬間を覚える(入ってくるアニメーション用)
        if (state != uiState)
        {
            uiState = state;
            uiStateTime = Time.unscaledTime;
            for (int c = 0; c < cardLift.Length; c++) cardLift[c] = 0f;
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

        string cpuSub = IsOnlineMode ? "ONLINE" : (savedDifficulty >= 0 ? difficultyNames[savedDifficulty] : "");
        bool meHost = !IsOnlineMode || IsHostOnline;
        // 色はロボットに合わせる(ホスト = 水色 / 参加した人 = オレンジ)
        Color cyanDark = new Color32(0x03, 0x14, 0x17, 0xFF), amberDark = new Color32(0x1F, 0x13, 0x00, 0xFF);
        Color meCol = meHost ? cyan : amber, meDark = meHost ? cyanDark : amberDark;
        Color rivalCol = meHost ? amber : cyan, rivalDark = meHost ? amberDark : cyanDark;
        DrawHudPanel(0, false, k, meHost ? "P1" : "P2", "YOU", "", playerStocks, player, playerWaiting, playerWeapon,
                     meCol, meDark);
        DrawHudPanel(1, true, k, meHost ? "P2" : "P1", IsOnlineMode ? "RIVAL" : "CPU", cpuSub, cpuStocks, cpu, cpuWaiting, cpuWeapon,
                     rivalCol, rivalDark);

        // 頭の上のマーカー(画面の外にいるときは、ふちに矢印)
        if (showMarkers && state != State.SelectDifficulty && state != State.SelectStage)
        {
            DrawMarker(player, playerWaiting, IsOnlineMode ? "YOU" : "P1", meCol, meDark, unit);
            DrawMarker(cpu, cpuWaiting, IsOnlineMode ? "RIVAL" : "CPU", rivalCol, rivalDark, unit);
        }

        // 部位が壊れたときの文字(頭上に出して、上に浮かびながら消える)
        DrawPopups(unit);

        // カウントダウン / FIGHT / 結果
        Rect center = new Rect(0, Screen.height * 0.3f, Screen.width, unit * 3f);
        if (state == State.SelectDifficulty)
        {
            DrawDifficultySelect(unit, cyan, white);
        }
        else if (IsOnlineMode && state != State.Result && (!fightersFound || net == null || !net.IsSpawned || (state == State.SelectStage && !IsHostOnline)))
        {
            // オンライン:相手を待っている / ホストがステージを選んでいる
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color32(0x07, 0x09, 0x0D, 0xD8));
            midStyle.normal.textColor = white;
            string msg = !fightersFound || net == null || !net.IsSpawned ? "WAITING FOR PLAYERS..." : "HOST IS CHOOSING THE STAGE...";
            bool blink = Mathf.FloorToInt(Time.unscaledTime * 2f) % 2 == 0;
            midStyle.normal.textColor = blink ? white : cyan;
            GUI.Label(new Rect(0, Screen.height * 0.42f, Screen.width, unit * 1.4f), msg, midStyle);
            if (!fightersFound || net == null || !net.IsSpawned)
            {
                tinyStyle.alignment = TextAnchor.MiddleCenter;
                tinyStyle.normal.textColor = new Color(0.55f, 0.6f, 0.66f);
                GUI.Label(new Rect(0, Screen.height * 0.42f + unit * 1.6f, Screen.width, unit), onlineDebug, tinyStyle);
                tinyStyle.alignment = TextAnchor.MiddleLeft;
            }
        }
        else if (state == State.SelectStage)
        {
            DrawStageSelect(unit, cyan, white);
        }
        else if (state == State.Countdown)
        {
            DrawCountdown(unit, meCol, white);
        }
        else if (state == State.Playing && fightTextTimer > 0f)
        {
            DrawFightText(unit, meCol);
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
        float age = UiAge;

        // 画面がだんだん暗くなる
        Fill(new Rect(0, 0, Screen.width, Screen.height), A(new Color32(0x07, 0x09, 0x0D, 0xC8), EaseOut(age / 0.4f)));

        // 勝敗:大きく出て、ちょうどいい大きさに止まる。後ろに光
        bool win = resultText == "YOU WIN";
        Color resultColor = win ? cyan : (resultText == "YOU LOSE" ? amber : white);
        float ty0 = Screen.height * 0.07f;
        float ta = EaseOut(age / 0.25f);
        float ts = Mathf.Lerp(1.9f, 1f, EaseBack(age / 0.4f));
        DrawGlow(new Rect(Screen.width / 2f - unit * 9f, ty0 - unit * 1.5f, unit * 18f, unit * 6f), A(resultColor, 0.18f * ta));
        // 横に走る線
        float band = EaseOut((age - 0.1f) / 0.4f);
        Fill(new Rect(Screen.width / 2f - Screen.width * 0.4f * band, ty0 + unit * 3.05f, Screen.width * 0.8f * band, 2f), A(resultColor, 0.8f));
        bigStyle.fontSize = Mathf.RoundToInt(unit * 2.4f * ts);
        bigStyle.normal.textColor = A(Color.black, 0.5f * ta);
        GUI.Label(new Rect(4, ty0 + 4, Screen.width, unit * 3f), resultText, bigStyle);
        bigStyle.normal.textColor = A(resultColor, ta);
        GUI.Label(new Rect(0, ty0, Screen.width, unit * 3f), resultText, bigStyle);
        bigStyle.fontSize = Mathf.RoundToInt(unit * 2.4f);

        // 1 つもストックを失わずに勝った:PERFECT
        bool perfect = win && playerStocks == stockCount;
        string news = perfect ? (string.IsNullOrEmpty(recordNews) ? "★ PERFECT ★" : "PERFECT   " + recordNews) : recordNews;
        if (!string.IsNullOrEmpty(news) && age > 0.5f)
        {
            bool on = Mathf.FloorToInt(Time.unscaledTime * 3f) % 2 == 0;
            tinyStyle.alignment = TextAnchor.MiddleCenter;
            tinyStyle.normal.textColor = on ? amber : white;
            // ★ は文字ではなく本物の星で描く
            string plain = news.Replace("★", "").Trim();
            Rect nr = new Rect(0, ty0 + unit * 3.25f, Screen.width, unit * 0.8f);
            GUI.Label(nr, plain, tinyStyle);
            if (perfect)
            {
                float tw = tinyStyle.CalcSize(new GUIContent(plain)).x;
                float ss = unit * 0.55f;
                DrawStar(new Rect(Screen.width / 2f - tw / 2f - ss * 1.6f, nr.center.y - ss / 2f, ss, ss), amber, true);
                DrawStar(new Rect(Screen.width / 2f + tw / 2f + ss * 0.6f, nr.center.y - ss / 2f, ss, ss), amber, true);
            }
            tinyStyle.alignment = TextAnchor.MiddleLeft;
        }

        // 戦績カード(下から出てくる)
        float w = unit * 16f, rowH = unit * 1.05f;
        string[] labels = { "DAMAGE DEALT", "KO", "PARTS BROKEN", "STOCKS LEFT" };
        int[] you = { damageTaken[1], stockCount - cpuStocks, partsLost[1], playerStocks };
        int[] them = { damageTaken[0], stockCount - playerStocks, partsLost[0], cpuStocks };

        float cardIn = EaseOut((age - 0.25f) / 0.4f);
        float x = (Screen.width - w) / 2f;
        float y = Screen.height * 0.08f + unit * 4.1f + (1f - cardIn) * unit * 1.5f;
        float h = rowH * (labels.Length + 1) + unit * 1.4f;
        Rect cardRect = new Rect(x, y, w, h);
        Fill(new Rect(x + 6, y + 8, w, h), A(Color.black, 0.35f * cardIn));
        Fill(cardRect, A(card, cardIn));
        Border(cardRect, A(line, cardIn), 1f);
        Fill(new Rect(x, y, w, unit * 0.1f), A(resultColor, cardIn));

        float colL = x + w * 0.42f, colR = x + w * 0.74f, colW = w * 0.22f;
        float ty = y + unit * 0.5f;

        // 見出し(YOU / CPU)
        smallStyle.alignment = TextAnchor.MiddleCenter;
        smallStyle.normal.textColor = A(cyan, cardIn);
        GUI.Label(new Rect(colL, ty, colW, rowH), "YOU", smallStyle);
        smallStyle.normal.textColor = A(amber, cardIn);
        GUI.Label(new Rect(colR, ty, colW, rowH), IsOnlineMode ? "RIVAL" : "CPU", smallStyle);
        ty += rowH;

        for (int i = 0; i < labels.Length; i++)
        {
            // 1 行ずつ出てきて、数字が 0 から数え上がる
            float rowT = (age - 0.5f - i * 0.12f) / 0.5f;
            float rowIn = EaseOut(rowT * 2f);
            float count = EaseOut(rowT);
            Fill(new Rect(x + unit * 0.6f, ty, w - unit * 1.2f, 1f), A(line, rowIn));
            tinyStyle.alignment = TextAnchor.MiddleLeft;
            tinyStyle.normal.textColor = A(grey, rowIn);
            GUI.Label(new Rect(x + unit * 0.8f + (1f - rowIn) * unit, ty, w * 0.4f, rowH), labels[i], tinyStyle);

            bool youBetter = you[i] > them[i], themBetter = them[i] > you[i];
            if (labels[i] == "STOCKS LEFT")
            {
                // 残りストックは星で
                DrawStockStars(new Rect(colL, ty, colW, rowH), you[i], cyan, rowIn);
                DrawStockStars(new Rect(colR, ty, colW, rowH), them[i], amber, rowIn);
            }
            else
            {
                smallStyle.normal.textColor = A(youBetter ? white : grey, rowIn);
                GUI.Label(new Rect(colL, ty, colW, rowH), Mathf.RoundToInt(you[i] * count).ToString(), smallStyle);
                smallStyle.normal.textColor = A(themBetter ? white : grey, rowIn);
                GUI.Label(new Rect(colR, ty, colW, rowH), Mathf.RoundToInt(them[i] * count).ToString(), smallStyle);
                // 勝っている方に小さな印
                if (rowT >= 1f && (youBetter || themBetter))
                {
                    Rect cr = youBetter ? new Rect(colL, ty, colW, rowH) : new Rect(colR, ty, colW, rowH);
                    Fill(new Rect(cr.center.x - unit * 0.6f, cr.yMax - unit * 0.18f, unit * 1.2f, 2f), youBetter ? cyan : amber);
                }
            }
            ty += rowH;
        }
        smallStyle.alignment = TextAnchor.MiddleLeft;

        // 試合時間と強さ
        int m = Mathf.FloorToInt(fightTime / 60f), sec = Mathf.FloorToInt(fightTime % 60f);
        string level = savedDifficulty >= 0 ? difficultyNames[savedDifficulty] : "";
        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = A(grey, cardIn);
        string info = IsOnlineMode ? "TIME  " + m + ":" + sec.ToString("00") + "      ONLINE MATCH" : "TIME  " + m + ":" + sec.ToString("00") + "      CPU  " + level;
        GUI.Label(new Rect(x, ty + unit * 0.15f, w, unit * 0.7f), info, tinyStyle);
        tinyStyle.alignment = TextAnchor.MiddleLeft;

        // ボタン(クリックでも、R / D / T キーでも)。少し遅れて出る
        float btnIn = EaseOut((age - 1.0f) / 0.3f);
        if (btnIn <= 0f) return;
        string[] btn = IsOnlineMode
            ? new[] { IsHostOnline && !opponentLeft ? "REMATCH  [R]" : (opponentLeft ? "OPPONENT LEFT" : "WAITING FOR HOST"), "LEAVE  [T]" }
            : new[] { "REMATCH  [R]", "CHANGE LEVEL  [D]", "TITLE  [T]" };
        int nb = btn.Length;
        float bw = unit * 6.2f, bh = unit * 1.4f, gap = unit * 0.4f;
        float bx = (Screen.width - (bw * nb + gap * (nb - 1))) / 2f;
        float by = y + h + unit * 0.8f + (1f - btnIn) * unit * 0.6f;
        for (int i = 0; i < nb; i++)
        {
            Rect r = new Rect(bx + i * (bw + gap), by, bw, bh);
            bool hover = r.Contains(Event.current.mousePosition);
            bool primary = i == 0;
            if (primary)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
                DrawGlow(ScaleRect(r, 1.5f), A(cyan, (0.12f + 0.1f * pulse) * btnIn));
            }
            Fill(r, A(primary ? (hover ? white : cyan) : card, btnIn));
            if (!primary) Border(r, A(hover ? cyan : line, btnIn), hover ? 2f : 1f);
            tinyStyle.alignment = TextAnchor.MiddleCenter;
            tinyStyle.normal.textColor = A(primary ? (Color)new Color32(0x03, 0x14, 0x17, 0xFF) : (hover ? cyan : white), btnIn);
            GUI.Label(r, btn[i], tinyStyle);
            tinyStyle.alignment = TextAnchor.MiddleLeft;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (IsOnlineMode)
                {
                    if (i == 0) Reload(true);
                    else GoToTitle();
                }
                else if (i == 0) Reload(true);
                else if (i == 1) Reload(false);
                else GoToTitle();
            }
        }
    }

    // 残りストックを星で並べる
    void DrawStockStars(Rect cell, int n, Color c, float alpha)
    {
        float ss = cell.height * 0.5f, sg = ss * 0.3f;
        float total = ss * stockCount + sg * (stockCount - 1);
        float sx = cell.center.x - total / 2f;
        for (int s = 0; s < stockCount; s++)
        {
            Rect st = new Rect(sx + s * (ss + sg), cell.center.y - ss / 2f, ss, ss);
            DrawStar(st, s < n ? A(c, alpha) : A(HudEmpty, alpha), s < n);
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

        string[] labels = IsOnlineMode
            ? new[] { "RESUME   [ESC]", "LEAVE MATCH   [T]" }
            : new[] { "RESUME   [ESC]", "RETRY   [R]", "TITLE   [T]" };
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
                else if (i == 1 && !IsOnlineMode) Reload(true);
                else GoToTitle();
            }
        }
    }

    // ステージ選び(カードの中に足場の形を小さく描く)
    void DrawStageSelect(float unit, Color accent, Color white)
    {
        Color card = new Color32(0x0D, 0x11, 0x17, 0xFF);
        Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);
        Color plat = new Color32(0x3A, 0x44, 0x52, 0xFF);
        Color grey = new Color(0.55f, 0.6f, 0.66f);
        // ステージごとの空の色(FACTORY 夜 / SPLIT 夕焼け / SHIFT 嵐)
        Color[] sky = { new Color32(0x10, 0x16, 0x24, 0xFF), new Color32(0x3A, 0x1E, 0x2C, 0xFF), new Color32(0x14, 0x1A, 0x22, 0xFF) };
        Color[] skyTop = { new Color32(0x07, 0x0A, 0x12, 0xFF), new Color32(0x1C, 0x10, 0x2A, 0xFF), new Color32(0x0A, 0x0D, 0x12, 0xFF) };

        DrawMenuBackdrop(accent, EaseOut(UiAge / 0.3f));
        DrawHeading("SELECT STAGE", Screen.height * 0.1f, unit, accent, white);

        int count = stageNames.Length;
        float gap = unit * 0.7f;
        float w = Mathf.Min(unit * 9f, (Screen.width - unit * 2f - gap * (count - 1)) / count), h = unit * 7.4f;
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
            float enter = EaseOut((UiAge - 0.08f * i) / 0.35f);
            Rect baseR = new Rect(x0 + i * (w + gap), y0 + (1f - enter) * unit * 2f, w, h);
            bool hover = baseR.Contains(Event.current.mousePosition) || menuIndex == i;   // マウスかカーソル
            if (Event.current.type == EventType.Repaint)
                cardLift[i] = Mathf.MoveTowards(cardLift[i], hover ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            float lift = EaseOut(cardLift[i]);
            Rect r = new Rect(baseR.x, baseR.y - lift * unit * 0.35f, w, h);

            Fill(new Rect(r.x + 6, r.y + 8, w, h), A(Color.black, 0.35f * enter));
            if (lift > 0.01f) DrawGlow(ScaleRect(r, 1.4f), A(accent, 0.1f * lift));
            Fill(r, A(card, enter));
            Fill(new Rect(r.x, r.y, w, unit * 0.12f), A(hover ? accent : line, enter));
            Border(r, hover ? accent : line, hover ? 2f : 1f);

            // 小さい地図(空のグラデーション + 足場)
            Rect map = new Rect(r.x + unit * 0.5f, r.y + unit * 0.55f, r.width - unit, h * 0.55f);
            int bands = 12;
            for (int bnd = 0; bnd < bands; bnd++)
                Fill(new Rect(map.x, map.y + map.height * bnd / bands, map.width, map.height / bands + 1f),
                     A(Color.Lerp(skyTop[i], sky[i], (float)bnd / (bands - 1)), enter));
            if (i == 1)   // 夕焼けの太陽
                DrawGlow(new Rect(map.x + map.width * 0.62f, map.y + map.height * 0.25f, map.height * 0.6f, map.height * 0.6f), A(new Color32(0xFF, 0x9A, 0x4A, 0xFF), 0.7f * enter));
            if (i == 2 && Mathf.Repeat(Time.unscaledTime, 3.2f) < 0.08f)   // 嵐の稲光
                Fill(map, A(Color.white, 0.25f));

            float sx = map.width / 22f, sy = map.height / 9f;
            float t = Time.unscaledTime;
            for (int p = 0; p < layouts[i].Length; p++)
            {
                float[] pl = layouts[i][p];
                float ox = 0f, oy = 0f;
                // SHIFT:動く足場は、地図の中でも動く
                if (i == 2 && p == 1) ox = Mathf.Sin(t / 5f * Mathf.PI * 2f) * 2.2f;
                if (i == 2 && p == 2) ox = Mathf.Sin((t / 5f + 0.5f) * Mathf.PI * 2f) * 2.2f;
                if (i == 2 && p == 3) oy = Mathf.Sin((t / 4f + 0.25f) * Mathf.PI * 2f) * 1.4f;
                float px = map.x + (pl[0] + ox - pl[2] / 2f + 11f) * sx;
                float py = map.y + (7.5f - pl[1] - oy) * sy;
                float ph = pl[2] >= 7f ? sy * 0.9f : Mathf.Max(3f, sy * 0.3f);
                Fill(new Rect(px, py, pl[2] * sx, ph), A(plat, enter));
                Fill(new Rect(px, py, pl[2] * sx, 1f), A(hover ? accent : grey, 0.5f * enter));
            }
            Border(map, A(line, enter), 1f);

            midStyle.normal.textColor = A(hover ? accent : white, enter);
            GUI.Label(new Rect(r.x, map.yMax + unit * 0.35f, w, unit * 1.1f), (i + 1) + "  " + stageNames[i], midStyle);

            tinyStyle.alignment = TextAnchor.MiddleCenter;
            tinyStyle.normal.textColor = A(grey, enter);
            GUI.Label(new Rect(r.x, map.yMax + unit * 1.45f, w, unit * 0.6f), stageNotes[i], tinyStyle);
            tinyStyle.alignment = TextAnchor.MiddleLeft;

            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) ChooseStage(i);
        }

        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = grey;
        string hint = IsOnlineMode ? "←  →  to choose      ENTER / A  to start" : "←  →  to choose      ENTER / A  to start      BACKSPACE / B  change level";
        GUI.Label(new Rect(0, y0 + h + unit * 0.9f, Screen.width, unit), hint, tinyStyle);
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

    // ===== カウントダウン(数字が大きく出て、縮みながら止まる + 輪が広がる) =====
    void DrawCountdown(float unit, Color accent, Color white)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(countdownLeft));
        float u = 1f - (countdownLeft - (n - 1));   // その数字が出てからの進み 0 → 1
        float cy = Screen.height * 0.38f;
        Rect box = new Rect(0, cy - unit * 2f, Screen.width, unit * 4f);

        // 輪
        float rs = unit * (4f + u * 5f);
        DrawRing(new Rect(Screen.width / 2f - rs / 2f, cy - rs / 2f, rs, rs), A(accent, 0.6f * (1f - u)));
        DrawGlow(new Rect(Screen.width / 2f - unit * 4f, cy - unit * 4f, unit * 8f, unit * 8f), A(accent, 0.15f));

        // 数字
        float s = Mathf.Lerp(1.8f, 1f, EaseOut(u / 0.35f));
        bigStyle.fontSize = Mathf.RoundToInt(unit * 3.2f * s);
        bigStyle.normal.textColor = A(Color.black, 0.5f);
        GUI.Label(new Rect(box.x + 4, box.y + 4, box.width, box.height), n.ToString(), bigStyle);
        bigStyle.normal.textColor = A(white, Mathf.Clamp01(u * 4f));
        GUI.Label(box, n.ToString(), bigStyle);
        bigStyle.fontSize = Mathf.RoundToInt(unit * 2.4f);

        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = A(accent, 0.8f);
        GUI.Label(new Rect(0, cy + unit * 2.4f, Screen.width, unit), "GET READY", tinyStyle);
        tinyStyle.alignment = TextAnchor.MiddleLeft;
    }

    // ===== FIGHT!(ズームで出て、横に光の帯が走る) =====
    void DrawFightText(float unit, Color accent)
    {
        float age = 0.8f - fightTextTimer;          // 0 → 0.8
        float cy = Screen.height * 0.38f;
        if (age < 0.12f) Fill(new Rect(0, 0, Screen.width, Screen.height), A(Color.white, 0.35f * (1f - age / 0.12f)));

        float band = EaseOut(age / 0.25f);
        float fade = Mathf.Clamp01((0.8f - age) / 0.25f);
        Fill(new Rect(0, cy - unit * 1.6f, Screen.width * band, 2f), A(accent, fade));
        Fill(new Rect(Screen.width * (1f - band), cy + unit * 1.6f, Screen.width * band, 2f), A(accent, fade));
        Fill(new Rect(0, cy - unit * 1.5f, Screen.width, unit * 3f), A(Color.black, 0.35f * fade * band));

        float s = Mathf.Lerp(2.4f, 1f, EaseBack(age / 0.3f));
        bigStyle.fontSize = Mathf.RoundToInt(unit * 2.8f * s);
        bigStyle.normal.textColor = A(accent, fade);
        GUI.Label(new Rect(0, cy - unit * 2f, Screen.width, unit * 4f), "FIGHT!", bigStyle);
        bigStyle.fontSize = Mathf.RoundToInt(unit * 2.4f);
    }

    // ===== 強さ選び(星の数で強さが分かるカード) =====
    void DrawDifficultySelect(float unit, Color accent, Color white)
    {
        Color card = new Color32(0x0D, 0x11, 0x17, 0xFF);
        Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);
        Color grey = new Color(0.55f, 0.6f, 0.66f);

        DrawMenuBackdrop(accent, EaseOut(UiAge / 0.3f));
        DrawHeading("SELECT CPU LEVEL", Screen.height * 0.16f, unit, accent, white);

        float w = unit * 6.4f, h = unit * 4.4f, gap = unit * 0.7f;
        float total = w * 3 + gap * 2;
        float x0 = (Screen.width - total) / 2f;
        float y0 = Screen.height * 0.34f;

        string[] notes = { "Slow and careless", "A fair fight", "Fast and ruthless" };
        Color[] tint = { new Color32(0x6E, 0xE7, 0xA0, 0xFF), accent, new Color32(0xFF, 0x5A, 0x5A, 0xFF) };

        for (int i = 0; i < 3; i++)
        {
            // 1 枚ずつ下から出てくる
            float enter = EaseOut((UiAge - 0.08f * i) / 0.35f);
            Rect baseR = new Rect(x0 + i * (w + gap), y0 + (1f - enter) * unit * 2f, w, h);
            bool hover = baseR.Contains(Event.current.mousePosition) || menuIndex == i;
            if (Event.current.type == EventType.Repaint)
                cardLift[i] = Mathf.MoveTowards(cardLift[i], hover ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            float lift = EaseOut(cardLift[i]);
            Rect r = new Rect(baseR.x, baseR.y - lift * unit * 0.35f, w, h);
            Color c = tint[i];

            // 影 → カード → 上の帯
            Fill(new Rect(r.x + 6, r.y + 8, w, h), A(Color.black, 0.35f * enter));
            if (lift > 0.01f) DrawGlow(ScaleRect(r, 1.5f), A(c, 0.12f * lift));
            Fill(r, A(card, enter));
            Fill(new Rect(r.x, r.y, w, unit * 0.12f), A(c, enter));
            Border(r, hover ? c : line, hover ? 2f : 1f);

            // 星(1 / 2 / 3 個)
            float ss = unit * 0.9f, sg = unit * 0.25f;
            float sw = ss * 3 + sg * 2, sx0 = r.x + (w - sw) / 2f;
            for (int s = 0; s < 3; s++)
            {
                Rect st = new Rect(sx0 + s * (ss + sg), r.y + h * 0.17f, ss, ss);
                bool on = s <= i;
                if (on && hover) DrawGlow(ScaleRect(st, 2.2f), A(c, 0.25f));
                float pop = on && hover ? 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f + s) : 1f;
                DrawStar(ScaleRect(st, pop), on ? A(c, enter) : A(line, enter), on);
            }

            midStyle.normal.textColor = A(hover ? c : white, enter);
            GUI.Label(new Rect(r.x, r.y + h * 0.44f, w, h * 0.25f), (i + 1) + "  " + difficultyNames[i], midStyle);

            tinyStyle.alignment = TextAnchor.MiddleCenter;
            tinyStyle.normal.textColor = A(grey, enter);
            GUI.Label(new Rect(r.x, r.y + h * 0.7f, w, h * 0.2f), notes[i], tinyStyle);
            tinyStyle.alignment = TextAnchor.MiddleLeft;

            // クリックでも選べる
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) ChooseDifficulty(i);
        }

        tinyStyle.alignment = TextAnchor.MiddleCenter;
        tinyStyle.normal.textColor = grey;
        GUI.Label(new Rect(0, y0 + h + unit * 1.0f, Screen.width, unit), "←  →  to choose      ENTER / A  to start      1 / 2 / 3", tinyStyle);
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


    // 上の真ん中のタイマー(残り 30 秒でオレンジ、10 秒で赤く脈打つ)
    void DrawTimer(float k, Color amber, Color white)
    {
        EnsureHudStyles();
        int m = Mathf.FloorToInt(timeLeft / 60f);
        int sec = Mathf.FloorToInt(timeLeft % 60f);
        bool playing = state == State.Playing;
        bool last10 = playing && timeLeft <= 10f && timeLeft > 0f;
        float beat = last10 ? 1f - (timeLeft % 1f) : 0f;          // 1 秒ごとに 1 → 0
        float s = last10 ? 1f + 0.18f * Mathf.Pow(1f - beat, 6f) : 1f;

        Rect r = ScaleRect(new Rect(Screen.width / 2f - 64 * k, 16 * k, 128 * k, 42 * k), s);
        if (last10) DrawGlow(ScaleRect(r, 2.2f), A(HudRed, 0.35f * (1f - beat)));
        Fill(r, HudPanel);
        Border(r, last10 ? HudRed : HudLine, last10 ? 2f : 1f);
        // 下の細い線:試合の残り時間
        float total = Mathf.Max(1f, matchTime);
        Fill(new Rect(r.x, r.yMax - 2f, r.width * Mathf.Clamp01(timeLeft / total), 2f), last10 ? HudRed : (playing && timeLeft <= 30f ? amber : A(white, 0.35f)));

        hudMono.fontSize = Mathf.RoundToInt(22 * k * s);
        hudMono.alignment = TextAnchor.MiddleCenter;
        hudMono.normal.textColor = last10 ? HudRed : (playing && timeLeft <= 30f ? amber : white);
        GUI.Label(r, m + ":" + sec.ToString("00"), hudMono);
        hudMono.alignment = TextAnchor.MiddleLeft;
    }

    // HP の表示用の値をなめらかに動かす(当たると白い「削れた分」が少し残ってから減る)
    void UpdateHpAnim(int side, int hp, int maxHp)
    {
        float dt = Time.unscaledDeltaTime;
        if (uiHpShown[side] < 0f) { uiHpShown[side] = hp; uiHpTrail[side] = hp; uiPrevHp[side] = hp; }
        if (hp < uiPrevHp[side])
        {
            uiHitAt[side] = Time.unscaledTime;
            uiTrailHold[side] = Time.unscaledTime + 0.45f;
        }
        uiPrevHp[side] = hp;
        uiHpShown[side] = Mathf.MoveTowards(uiHpShown[side], hp, maxHp * 4f * dt);
        if (hp > uiHpTrail[side]) uiHpTrail[side] = hp;   // 回復はすぐ
        else if (Time.unscaledTime > uiTrailHold[side]) uiHpTrail[side] = Mathf.MoveTowards(uiHpTrail[side], hp, maxHp * 0.9f * dt);
    }

    // 下の左右のパネル(名前・ストック★・HP・武器と熱・部位)
    void DrawHudPanel(int side, bool rightSide, float k, string badge, string name, string sub,
                      int stocks, HealthSystem fighter, bool waiting, WeaponSystem weapon,
                      Color accent, Color badgeText)
    {
        EnsureHudStyles();

        int n = weapon != null ? weapon.WeaponCount : 0;
        int rows = Mathf.Max(1, Mathf.CeilToInt(n / 3f));
        float W = 420f, H = 66f + rows * 44f - 6f;     // 武器3つで1段、6つで2段
        float x0 = rightSide ? Screen.width / k - W - 20f : 20f;
        float y0 = Screen.height / k - H - 14f;

        int hp = (fighter != null && !waiting) ? fighter.currentHealth : 0;
        int maxHp = fighter != null ? Mathf.Max(1, fighter.maxHealth) : 100;
        UpdateHpAnim(side, hp, maxHp);

        // 当たった直後はパネルが少しゆれる
        float hitAge = Time.unscaledTime - uiHitAt[side];
        if (hitAge < 0.25f)
        {
            float amt = 4f * (1f - hitAge / 0.25f);
            x0 += Random.Range(-amt, amt);
            y0 += Random.Range(-amt, amt) * 0.5f;
        }

        // パネル(内側のはしに色の帯、上に色の線)
        Rect panel = R(k, x0, y0, W, H);
        Fill(panel, HudPanel);
        Border(panel, HudLine, 1f);
        Fill(R(k, rightSide ? x0 + W - 3 : x0, y0, 3, H), accent);
        Fill(R(k, x0, y0, W, 1), A(accent, 0.45f));
        if (hitAge < 0.15f) Fill(panel, A(HudRed, 0.18f * (1f - hitAge / 0.15f)));   // 当たった瞬間、赤く光る

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
        float nameW = hudName.CalcSize(new GUIContent(name)).x / k;
        if (sub.Length > 0)
        {
            hudMono.fontSize = Mathf.RoundToInt(10 * k);
            hudMono.normal.textColor = HudLabel;
            GUI.Label(R(k, x0 + 54 + nameW + 8, y0 + 13, 70, 22), sub, hudMono);
        }

        // ストック(本物の星)。減った瞬間は、白く光りながら大きくなって消える
        if (uiPrevStocks[side] >= 0 && stocks < uiPrevStocks[side])
        {
            uiStockLostAt[side] = Time.unscaledTime;
            uiStockLostIdx[side] = stocks;
        }
        uiPrevStocks[side] = stocks;
        float lostAge = Time.unscaledTime - uiStockLostAt[side];
        for (int i = 0; i < stockCount; i++)
        {
            Rect st = R(k, x0 + 172 + i * 20, y0 + 13, 16, 16);
            if (i < stocks)
            {
                // 最後の1つは、ドキドキ脈打つ
                float pulse = stocks == 1 ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
                DrawGlow(ScaleRect(st, 2f), A(accent, 0.18f * pulse));
                DrawStar(st, A(accent, pulse), true);
            }
            else
            {
                DrawStar(st, HudEmpty, false);
                if (i == uiStockLostIdx[side] && lostAge < 0.6f)
                {
                    float u = lostAge / 0.6f;
                    DrawStar(ScaleRect(st, 1f + u * 1.4f), A(Color.white, 1f - u), true);
                    DrawGlow(ScaleRect(st, 2f + u * 3f), A(accent, 0.5f * (1f - u)));
                }
            }
        }

        // HP の数字(当たった直後は少しゆれて赤く)
        bool low = hp <= maxHp * 0.3f;
        hudMono.fontSize = Mathf.RoundToInt(11 * k);
        hudMono.normal.textColor = HudLabel;
        hudMono.alignment = TextAnchor.MiddleRight;
        GUI.Label(R(k, x0 + W - 110, y0 + 12, 48, 24), "HP", hudMono);
        hudMono.alignment = TextAnchor.MiddleLeft;
        hudNum.fontSize = Mathf.RoundToInt((26 + (hitAge < 0.2f ? 4f * (1f - hitAge / 0.2f) : 0f)) * k);
        hudNum.normal.textColor = hitAge < 0.12f ? HudRed : (low ? HudHot : HudWhite);
        GUI.Label(R(k, x0 + W - 100, y0 + 6, 86, 34), Mathf.RoundToInt(uiHpShown[side]).ToString(), hudNum);

        // HP バー:背景 → 削れた分(白) → 今の HP。10% ごとに目盛り。ピンチのときは脈打つ
        float bx = x0 + 14, by = y0 + 42, bw = W - 28, bh = 7;
        Fill(R(k, bx, by, bw, bh), HudLine);
        float trailR = Mathf.Clamp01(uiHpTrail[side] / maxHp), shownR = Mathf.Clamp01(uiHpShown[side] / maxHp);
        Fill(R(k, bx, by, bw * trailR, bh), A(Color.white, 0.75f));
        Color barCol = low ? Color.Lerp(HudHot, HudRed, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f)) : accent;
        Fill(R(k, bx, by, bw * shownR, bh), barCol);
        Fill(R(k, bx, by, bw * shownR, 2), A(Color.white, 0.25f));   // 上のつや
        for (int t = 1; t < 10; t++) Fill(R(k, bx + bw * t / 10f, by, 1, bh), A(HudPanel, 0.8f));
        if (low && hp > 0) DrawGlow(R(k, bx - 10, by - 14, bw * shownR + 20, bh + 28), A(HudRed, 0.12f));

        // 武器スロット(3つずつ並べる)
        for (int i = 0; i < n; i++)
        {
            float sx = x0 + 14 + (i % 3) * 112, sy = y0 + 58 + (i / 3) * 44;
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
                float py = y0 + 64 + i * 12;
                float ratio = waiting ? 1f : Mathf.Clamp01(ratios[i]);
                bool broken = ratio <= 0f;
                bool blinkOn = Mathf.FloorToInt(Time.unscaledTime * 4f) % 2 == 0;
                hudMono.fontSize = Mathf.RoundToInt(9 * k);
                hudMono.normal.textColor = broken ? (blinkOn ? HudRed : HudLabel) : HudLabel;
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
        // 持っている武器:ふんわり光る
        if (current && !switching)
        {
            float glow = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
            Fill(r, A(accent, 0.07f + 0.05f * glow));
            Border(r, accent, 1f);
            Fill(R(k, sx, sy, 3, sh), accent);
        }
        else Border(r, HudLine, 1f);
        // オーバーヒート中:赤く点滅
        if (locked && Mathf.FloorToInt(Time.unscaledTime * 3f) % 2 == 0) Fill(r, A(HudRed, 0.12f));
        // 熱い(70 以上):オレンジの縁
        if (!locked && heat >= 70f) Border(r, A(HudHot, 0.6f), 1f);

        // 持ち替え中:下に進み具合のバー
        if (switching) Fill(R(k, sx, sy + sh - 2, sw * weapon.SwitchProgress, 2), accent);

        // アイコン(弾の飛び方の線)
        Color ic = locked ? HudRed : (current ? accent : HudGrey);
        DrawWeaponIcon(k, wn, sx + 5, sy + 11, ic);

        // 名前
        hudMono.fontSize = Mathf.RoundToInt(9.5f * k);
        hudMono.normal.textColor = locked ? HudRed : (current ? HudWhite : HudGrey);
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

    // =====================================================================
    // オンライン対戦
    // =====================================================================
    void OnlineUpdate()
    {
        // 2人のロボットを探す(自分 = player / 相手 = cpu)
        if (!fightersFound) FindOnlineFighters();

        // ポーズ = 抜けるメニュー(自分の画面だけ。試合は止まらない)
        // 待っている間・ステージ選びの間も開けるので、2P もいつでも抜けられる
        if (state != State.Result)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.JoystickButton7))
            {
                if (paused) Resume(); else Pause();
            }
            else if (paused)
            {
                if (Input.GetKeyDown(KeyCode.JoystickButton0)) Resume();
                else if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.JoystickButton1)) GoToTitle();
            }
        }
        else if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
        {
            GoToTitle();   // 結果画面からも抜けられる(通信が切れたあとも)
            return;
        }

        // 相手が抜けた / 通信が切れた(ホストが抜けると、2P 側はここに来る)
        var nmNow = NetworkManager.Singleton;
        bool disconnected = nmNow == null || !nmNow.IsListening || net == null || !net.IsSpawned;
        bool robotGone = fightersFound && (player == null || cpu == null);
        if ((robotGone || (disconnected && (fightersFound || appliedStage >= 0))) && state != State.Result && !opponentLeft)
        {
            opponentLeft = true;
            OnlineStateChanged(State.Result);
            return;
        }
        if (disconnected) return;

        // ホストが決めたステージを、自分の画面にも出す
        if (net.Stage >= 0 && net.Stage != appliedStage)
        {
            appliedStage = net.Stage;
            savedStage = net.Stage;
            ApplyStage(net.Stage);
            var fx = FindFirstObjectByType<BackgroundFX>();
            if (fx != null) fx.SetMood(net.Stage);
        }

        // 試合の状態をホストに合わせる
        State ns = (State)(net.State + 1);
        if (opponentLeft) ns = State.Result;
        if (ns != state) OnlineStateChanged(ns);

        // ストックは2人とも同じ値を見る
        if (fightersFound && player != null && cpu != null)
        {
            playerStocks = net.StocksOf(player.OwnerClientId);
            cpuStocks = net.StocksOf(cpu.OwnerClientId);
        }
        playerWaiting = player != null && player.currentHealth <= 0 && state == State.Playing;
        cpuWaiting = cpu != null && cpu.currentHealth <= 0 && state == State.Playing;

        switch (state)
        {
            case State.SelectStage:
                if (!IsHostOnline || !fightersFound) break;
                if (skipSelect) { skipSelect = false; ChooseStage(savedStage); break; }   // 再戦は同じステージ
                if (Input.GetKeyDown(KeyCode.Alpha1)) ChooseStage(0);
                else if (Input.GetKeyDown(KeyCode.Alpha2)) ChooseStage(1);
                else if (Input.GetKeyDown(KeyCode.Alpha3)) ChooseStage(2);
                else if (MenuMove(stageNames.Length)) { }
                else if (MenuConfirm()) ChooseStage(menuIndex);
                break;

            case State.Countdown:
                {
                    int before = Mathf.CeilToInt(countdownLeft);
                    countdownLeft = net.TimeToPhaseEnd;
                    int after = Mathf.CeilToInt(countdownLeft);
                    if (after != before && after > 0) SoundManager.Play("count");
                    if (IsHostOnline && countdownLeft <= 0f) net.ServerStartFight(matchTime);
                }
                break;

            case State.Playing:
                if (fightTextTimer > 0f) fightTextTimer -= Time.deltaTime;
                fightTime += Time.deltaTime;
                timeLeft = net.TimeToPhaseEnd;
                TrackDamage(player, 0);
                TrackDamage(cpu, 1);

                if (IsHostOnline)
                {
                    if (timeLeft <= 0f) HostTimeUp();
                    else
                    {
                        HostCheckKO(player, 0);
                        HostCheckKO(cpu, 1);
                    }
                }
                break;

            case State.Result:
                if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.JoystickButton0)) Reload(true);
                else if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.JoystickButton1)) GoToTitle();
                break;
        }
    }

    // うまくつながらないときに、待ち画面に出す情報
    private string onlineDebug = "";

    void FindOnlineFighters()
    {
        int total = 0, spawned = 0, mine = 0;
        foreach (HealthSystem h in FindObjectsByType<HealthSystem>(FindObjectsSortMode.None))
        {
            total++;
            if (h.IsSpawned) spawned++;
            if (h.IsSpawned && h.IsOwner) mine++;
        }
        var nmDbg = NetworkManager.Singleton;
        onlineDebug = "match " + (net != null && net.IsSpawned ? "OK" : "NOT SPAWNED")
                    + "   robots " + total + "   online " + spawned + "   mine " + mine
                    + "   " + (nmDbg == null ? "no NetworkManager" : (nmDbg.IsHost ? "HOST" : nmDbg.IsClient ? "CLIENT" : "OFFLINE"));

        foreach (HealthSystem h in FindObjectsByType<HealthSystem>(FindObjectsSortMode.None))
        {
            if (!h.IsSpawned) continue;
            if (h.IsOwner) player = h;
            else cpu = h;
        }
        if (player == null || cpu == null) return;

        fightersFound = true;
        Debug.Log("[Online] みつけた  自分=" + player.name + " (id " + player.OwnerClientId + ")  相手=" + cpu.name + " (id " + cpu.OwnerClientId + ")");
        playerWeapon = FindWeapon(player);
        cpuWeapon = FindWeapon(cpu);

        // 部位が壊れたときの文字(オンラインでも出す)
        player.OnPartBroken -= HandlePartBroken;
        cpu.OnPartBroken -= HandlePartBroken;
        player.OnPartBroken += HandlePartBroken;
        cpu.OnPartBroken += HandlePartBroken;

        // アイテムを出すのはホストだけ(出す場所と種類を2人に送る)
        if (spawnItems && net != null && net.IsServer && GetComponent<ItemSpawner>() == null) gameObject.AddComponent<ItemSpawner>();

        // 参加した人(P2)のロボットをオレンジの見た目にする
        ApplyOnlineSkin(player);
        ApplyOnlineSkin(cpu);

        // カメラが2人を映すようにする
        var cf = FindFirstObjectByType<CameraFollow>();
        if (cf != null) cf.targets = new[] { player.transform, cpu.transform };

        bool fighting = state == State.Playing;
        SetControl(player, fighting);
        SetControl(cpu, fighting);
    }

    void ApplyOnlineSkin(HealthSystem f)
    {
        if (f == null || f.OwnerClientId == NetworkManager.ServerClientId) return;   // ホストはそのまま(水色)
        var body = f.GetComponent<SpriteRenderer>();
        if (body != null && p2BodySprite != null) body.sprite = p2BodySprite;
        if (p2GunSprite == null) return;
        foreach (WeaponSystem w in f.GetComponentsInChildren<WeaponSystem>(true))
        {
            var sr = w.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sprite = p2GunSprite;
        }
    }

    void OnlineStateChanged(State ns)
    {
        state = ns;
        switch (ns)
        {
            case State.Countdown:
                countdownLeft = net.TimeToPhaseEnd;
                fightTime = 0f;
                SoundManager.Play("count");
                SetControl(player, false);
                SetControl(cpu, false);
                break;

            case State.Playing:
                SoundManager.Play("fight");
                fightTextTimer = 0.8f;
                if (!paused) SetControl(player, true);
                SetControl(cpu, true);
                break;

            case State.Result:
                paused = false;
                string text;
                if (opponentLeft) text = "OPPONENT LEFT";
                else if (net.Winner == -2) text = "DRAW";
                else text = (ulong)Mathf.Max(0, net.Winner) == NetworkManager.Singleton.LocalClientId ? "YOU WIN" : "YOU LOSE";
                EndMatch(text);
                break;
        }
    }

    // ホストがステージを決めて、カウントダウンを始める
    void HostBegin(int index)
    {
        savedStage = index;
        SoundManager.Play("ui");
        hostWaiting[0] = hostWaiting[1] = false;
        net.ServerStartCountdown(index, stockCount, countdownTime);
        // 2人ともスタート位置で、HP 満タンから
        foreach (HealthSystem f in new[] { player, cpu })
        {
            if (f == null) continue;
            var sp = f.GetComponent<NetworkPlayerSpawn>();
            f.ServerRespawn(sp != null ? sp.GetSpawnPosition() : (Vector3)respawnPoint, 0f);
        }
        graceUntil[0] = graceUntil[1] = Time.time + 1f;
    }

    void HostCheckKO(HealthSystem fighter, int i)
    {
        if (fighter == null || hostWaiting[i] || Time.time < graceUntil[i]) return;
        Vector3 p = fighter.transform.position;
        bool outOfBounds = p.x < blastLeft || p.x > blastRight || p.y < blastBottom || p.y > blastTop;
        if (outOfBounds || fighter.currentHealth <= 0) HostKnockOut(fighter, i);
    }

    void HostKnockOut(HealthSystem fighter, int i)
    {
        hostWaiting[i] = true;
        ulong id = fighter.OwnerClientId;
        net.KoRpc(id, fighter.transform.position);
        fighter.ServerKnockOut();

        int left = net.ServerLoseStock(id);
        if (left <= 0)
        {
            HealthSystem other = fighter == player ? cpu : player;
            net.ServerEnd(other != null ? (int)other.OwnerClientId : -2);
            return;
        }
        StartCoroutine(HostRespawn(fighter, i));
    }

    IEnumerator HostRespawn(HealthSystem fighter, int i)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (net == null || net.State != OnlineMatch.Playing || fighter == null) yield break;
        fighter.ServerRespawn(respawnPoint, invincibleTime);
        graceUntil[i] = Time.time + 1f;   // 移動が相手の画面に届くまで、場外判定を待つ
        hostWaiting[i] = false;
    }

    void HostTimeUp()
    {
        if (player == null || cpu == null) return;
        int ps = net.StocksOf(player.OwnerClientId), cs = net.StocksOf(cpu.OwnerClientId);
        int win;
        if (ps != cs) win = ps > cs ? (int)player.OwnerClientId : (int)cpu.OwnerClientId;
        else if (player.currentHealth != cpu.currentHealth) win = player.currentHealth > cpu.currentHealth ? (int)player.OwnerClientId : (int)cpu.OwnerClientId;
        else win = -2;
        net.ServerEnd(win);
    }

    // ===== オンラインのアイテム =====
    private int nextItemId = 1;

    // ItemSpawner(ホスト)から呼ばれる
    public void OnlineSpawnItem(ItemPickup.Kind kind, Vector2 pos)
    {
        if (!IsHostOnline) return;
        net.SpawnItemRpc(nextItemId++, (int)kind, pos);
    }

    void OnNetItemSpawned(int id, int kind, Vector2 pos)
    {
        ItemPickup item = ItemPickup.Create((ItemPickup.Kind)kind, pos);
        item.netId = id;
    }

    // ロボットがアイテムにさわった(取ったことにするのはホストだけ)
    public void OnlineItemTouched(ItemPickup item, HealthSystem who)
    {
        if (!IsHostOnline || item == null || who == null || !who.IsSpawned) return;
        if (takenItems.Contains(item.netId)) return;   // 2回取らない
        takenItems.Add(item.netId);
        net.ItemTakenRpc(item.netId, who.OwnerClientId);
    }
    private readonly System.Collections.Generic.HashSet<int> takenItems = new System.Collections.Generic.HashSet<int>();

    void OnNetItemTaken(int id, ulong clientId)
    {
        HealthSystem who = player != null && player.OwnerClientId == clientId ? player : cpu;
        foreach (ItemPickup it in ItemPickup.Active.ToArray())
        {
            if (it != null && it.netId == id)
            {
                if (who != null) it.ApplyOnline(who);
                else Destroy(it.gameObject);
                return;
            }
        }
    }

    // KO の演出(2人の画面で出る)
    void OnNetKo(ulong clientId, Vector2 pos)
    {
        bool mine = NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId;
        HealthSystem who = mine ? player : cpu;
        // ホスト = 水色 / 参加した人 = オレンジ
        Color c = clientId == NetworkManager.ServerClientId ? new Color32(0x4F, 0xE3, 0xF0, 0xFF) : new Color32(0xFF, 0xB5, 0x47, 0xFF);

        CameraFollow.Shake(0.5f, 0.35f);
        if (who != null) ShowPopup(who.transform, "KO!", c, false);
        Vector2 burstPos = new Vector2(Mathf.Clamp(pos.x, blastLeft + 2f, blastRight - 2f), Mathf.Clamp(pos.y, blastBottom + 2f, blastTop - 2f));
        HitEffect.Burst(burstPos, c);
        SoundManager.Play("ko");
        lastHp[mine ? 0 : 1] = -1;   // 場外 KO の分はダメージに数えない
    }

    // =====================================================================
    // UI の演出(星・動き・アニメーション)
    // =====================================================================
    private static Texture2D starTex, starLineTex, ringTex, glowTex;

    // 本物の星の形(5つのとがり)をプログラムで描く。outline = true なら枠だけ
    static Texture2D MakeStarTexture(bool outline)
    {
        const int N = 64;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        Vector2[] outer = StarPoly(N / 2f, N / 2f, N * 0.48f);
        Vector2[] inner = StarPoly(N / 2f, N / 2f, N * 0.30f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 4x4 に細かく調べて、ふちをなめらかにする
                int hit = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        Vector2 p = new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                        bool inO = InPoly(p, outer);
                        bool ok = outline ? inO && !InPoly(p, inner) : inO;
                        if (ok) hit++;
                    }
                t.SetPixel(x, y, new Color(1, 1, 1, hit / 16f));
            }
        t.Apply();
        return t;
    }

    static Vector2[] StarPoly(float cx, float cy, float r)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = (90f + i * 36f) * Mathf.Deg2Rad;
            float rr = i % 2 == 0 ? r : r * 0.42f;
            pts[i] = new Vector2(cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr);
        }
        return pts;
    }

    static bool InPoly(Vector2 p, Vector2[] poly)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)) c = !c;
        }
        return c;
    }

    // 丸い輪(カウントダウンの数字のまわり)と、ふんわりした光
    static Texture2D MakeRoundTexture(bool ring)
    {
        const int N = 128;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f, N / 2f)) / (N / 2f);
                float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) * 22f) : Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        t.Apply();
        return t;
    }

    void DrawTex(Rect r, Texture2D t, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, t);
        GUI.color = prev;
    }

    void DrawStar(Rect r, Color c, bool filled)
    {
        if (starTex == null) { starTex = MakeStarTexture(false); starLineTex = MakeStarTexture(true); }
        DrawTex(r, filled ? starTex : starLineTex, c);
    }

    void DrawRing(Rect r, Color c)
    {
        if (ringTex == null) ringTex = MakeRoundTexture(true);
        DrawTex(r, ringTex, c);
    }

    void DrawGlow(Rect r, Color c)
    {
        if (glowTex == null) glowTex = MakeRoundTexture(false);
        DrawTex(r, glowTex, c);
    }

    static Rect ScaleRect(Rect r, float s)
    {
        return new Rect(r.center.x - r.width * s / 2f, r.center.y - r.height * s / 2f, r.width * s, r.height * s);
    }

    static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
    static float EaseBack(float t) { t = Mathf.Clamp01(t); float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
    static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

    // 画面が切り替わった時刻(入ってくるアニメーションに使う)
    private State uiState = (State)(-1);
    private float uiStateTime;
    float UiAge => Time.unscaledTime - uiStateTime;

    // 左右のパネルの動き [0] = 左(自分) / [1] = 右
    private readonly int[] uiPrevStocks = { -1, -1 };
    private readonly float[] uiStockLostAt = { -10f, -10f };
    private readonly int[] uiStockLostIdx = { 0, 0 };
    private readonly float[] uiHpShown = { -1f, -1f };
    private readonly float[] uiHpTrail = { -1f, -1f };
    private readonly float[] uiTrailHold = { 0f, 0f };
    private readonly float[] uiHitAt = { -10f, -10f };
    private readonly int[] uiPrevHp = { -1, -1 };

    // カードの浮き上がり(マウスを乗せると少し上がる)
    private readonly float[] cardLift = new float[4];

    // 選ぶ画面の背景:ゆっくり流れる横線
    void DrawMenuBackdrop(Color accent, float alpha)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.027f, 0.035f, 0.05f, 0.9f * alpha));
        float gap = Screen.height / 36f;
        float off = (Time.unscaledTime * 18f) % gap;
        for (float y = -gap + off; y < Screen.height; y += gap)
            Fill(new Rect(0, y, Screen.width, 1f), A(accent, 0.035f * alpha));
        // 上下のふちの光
        Fill(new Rect(0, 0, Screen.width, 2f), A(accent, 0.6f * alpha));
        Fill(new Rect(0, Screen.height - 2f, Screen.width, 2f), A(accent, 0.6f * alpha));
    }

    // 見出し(真ん中から広がる下線つき)
    void DrawHeading(string text, float y, float unit, Color accent, Color white)
    {
        float a = EaseOut(UiAge / 0.35f);
        midStyle.normal.textColor = A(white, a);
        GUI.Label(new Rect(0, y - (1f - a) * unit * 0.6f, Screen.width, unit * 1.4f), text, midStyle);
        float lw = unit * 6f * EaseOut((UiAge - 0.1f) / 0.4f);
        Fill(new Rect(Screen.width / 2f - lw / 2f, y + unit * 1.35f, lw, 2f), accent);
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