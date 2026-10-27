using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    [Header("モード設定")]
    public bool isSoloMode = false;

    [Tooltip("CPU が操作するときはオン(CpuController が自動でオンにする)")]
    public bool isCpu = false;

    [Header("対戦設定")]
    public int playerID = 1; // 1Pなら1、2Pなら2をInspectorで設定(ローカル対戦用)

    [Header("基本移動")]
    public float moveSpeed = 8f;
    public float jumpForce = 12f;
    [Tooltip("空中でジャンプできる回数(1 なら2段ジャンプ)")]
    public int maxAirJumps = 1;
    private int airJumpsLeft;

    [Header("接地判定")]
    [Tooltip("この角度までの坂を「地面」とみなす")]
    [Range(0f, 89f)] public float maxGroundAngle = 45f;

    [Header("壁蹴り設定")]
    public float wallJumpForce = 10f;
    public float wallJumpSideForce = 12f;
    public LayerMask wallLayer;
    public Transform wallCheck;
    [Tooltip("壁にふれて、壁の方へ押しながら落ちるときの速さの上限(壁ずり)")]
    public float wallSlideSpeed = 2.5f;
    [Tooltip("壁キックのあと、操作が効きにくい時間(秒)。すぐ壁に戻らないように")]
    public float wallJumpLockTime = 0.15f;

    private int wallSide;                 // -1 = 左に壁 / 1 = 右に壁 / 0 = なし
    private float lastWallTime = -10f;
    private int lastWallSide;
    private float wallJumpLockUntil = -10f;
    private static readonly ContactPoint2D[] contactBuf = new ContactPoint2D[16];

    // 体にふれている「縦の面」(壁)を調べる。レイヤーの設定がなくても動く
    // (法線 = 面から自分の方を向く矢印。右を向いていれば、左に壁がある)
    int FindWallSide()
    {
        int n = rb.GetContacts(contactBuf);
        for (int i = 0; i < n; i++)
        {
            Collider2D other = contactBuf[i].collider;
            if (other == null || other.isTrigger) continue;
            if (other.attachedRigidbody == rb) other = contactBuf[i].otherCollider;
            if (other != null && other.GetComponentInParent<HealthSystem>() != null) continue;   // 相手のロボットは壁ではない
            Vector2 nrm = contactBuf[i].normal;
            if (nrm.x > 0.7f) return -1;
            if (nrm.x < -0.7f) return 1;
        }
        return 0;
    }

    [Header("ダッシュ設定")]
    public float dashSpeed = 20f;
    public float dashTime = 0.2f;
    private bool isDashing = false;

    [Header("手ざわり(ジャンプ)")]
    [Tooltip("床から落ちたあと、この時間(秒)ならまだジャンプできる")]
    public float coyoteTime = 0.1f;
    [Tooltip("着地の少し前にジャンプを押しても、着地した瞬間に跳ぶ(秒)")]
    public float jumpBufferTime = 0.12f;
    [Tooltip("ジャンプボタンを早く離すと、上昇がこの割合まで弱まる(小ジャンプ)")]
    [Range(0.1f, 1f)] public float jumpCutMultiplier = 0.5f;
    [Tooltip("落ちるときの重力の倍率(大きいほど、ふわっとせずにストンと落ちる)")]
    public float fallGravityMultiplier = 1.7f;
    [Tooltip("落ちる速さの上限")]
    public float maxFallSpeed = 18f;

    [Header("手ざわり(移動)")]
    [Tooltip("地面で動き出すときの加速")]
    public float groundAcceleration = 90f;
    [Tooltip("地面で止まるときの減速")]
    public float groundDeceleration = 70f;
    [Tooltip("空中での加速(小さいほど、空中で向きを変えにくい)")]
    public float airAcceleration = 45f;
    [Tooltip("撃たれて吹き飛んでいる間、操作が効きにくくなる時間(秒)")]
    public float knockbackControlTime = 0.25f;
    [Tooltip("ダッシュのあと、次のダッシュまで待つ時間(秒)")]
    public float dashCooldown = 0.35f;

    private float lastGroundedTime = -10f;
    private float jumpBufferedUntil = -10f;
    private bool jumpedSinceGrounded;
    private bool jumpRising;          // 自分のジャンプで上がっている途中か(小ジャンプの判定用)
    private float knockbackUntil = -10f;
    private float nextDashTime;
    private float baseGravity = 1f;

    // HealthSystem から呼ばれる:撃たれて吹き飛んだ
    public void NotifyKnockback()
    {
        knockbackUntil = Time.time + knockbackControlTime;
        jumpRising = false;
    }

    [Header("状態確認")]
    public PlayerState currentState = PlayerState.Idle;

    // 武器(ROUTE で線を描いている間など)から変えられる移動速度の倍率
    [HideInInspector] public float externalSpeedMul = 1f;

    private Rigidbody2D rb;
    private HealthSystem health;
    private float moveInput;

    // 脚が壊れていると、移動 80%・ジャンプの高さ 70%(速さは約 84%)
    private float SpeedMul => (health != null && health.LegBroken) ? 0.8f : 1f;
    private float JumpMul => (health != null && health.LegBroken) ? 0.84f : 1f;
    private bool isTouchingWall;
    private bool isGrounded;
    private ContactFilter2D groundFilter;
    private KeyCode dashKey;

    // CPU から渡される入力
    private float cpuMove;
    private bool cpuJumpRequest;

    // オンライン中か / このプレイヤーを操作してよいか
    private bool IsOnline => IsSpawned;
    private bool CanControl => !IsOnline || IsOwner;

    // CPU が「地面にいるか」を知るため
    public bool IsGrounded => isGrounded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<HealthSystem>();
        baseGravity = rb.gravityScale;

        // 足元に「上向きの面」が接しているかで接地を判定する
        groundFilter = new ContactFilter2D();
        groundFilter.useTriggers = false;
        groundFilter.SetNormalAngle(90f - maxGroundAngle, 90f + maxGroundAngle);
    }

    void Start()
    {
        // ソロ・ローカル対戦ではNetwork Rigidbody 2DがKinematicにしてしまうので、Dynamicに戻す
        // (オンライン時はNetwork Rigidbody 2Dが自動で正しく切り替える)
        if (!IsOnline) rb.bodyType = RigidbodyType2D.Dynamic;

        dashKey = (playerID == 1) ? KeyCode.LeftShift : KeyCode.RightShift;

        if (!gameObject.CompareTag("Player"))
        {
            Debug.LogWarning(gameObject.name + " のTagを 'Player' に設定してください！");
        }

        if (isSoloMode && playerID == 2 && !isCpu)
        {
            gameObject.SetActive(false);
        }
    }

    // CPU がダッシュするとき(CpuController から呼ばれる)
    public void CpuDash()
    {
        if (!isCpu || isDashing || Mathf.Abs(cpuMove) < 0.1f) return;
        moveInput = cpuMove;
        StartCoroutine(Dash());
    }

    // CPU から毎フレーム呼ばれる
    public void SetCpuInput(float move, bool jump)
    {
        cpuMove = Mathf.Clamp(move, -1f, 1f);
        if (jump) cpuJumpRequest = true;
    }

    void Update()
    {
        // ソロ・ローカル対戦では常に操作可能、オンラインでは自分のプレイヤーだけ
        if (!CanControl) return;

        if (isDashing) return;

        isGrounded = rb.IsTouching(groundFilter);
        if (isGrounded && rb.linearVelocity.y <= 0.1f)
        {
            airJumpsLeft = maxAirJumps;
            lastGroundedTime = Time.time;
            jumpedSinceGrounded = false;
        }
        wallSide = isGrounded ? 0 : FindWallSide();
        if (wallSide != 0) { lastWallTime = Time.time; lastWallSide = wallSide; }
        bool layerWall = wallCheck != null && wallLayer.value != 0 &&
                         Physics2D.OverlapCircle(wallCheck.position, 0.2f, wallLayer);
        // 壁から離れた直後(0.08 秒)も、壁キックできる
        isTouchingWall = !isGrounded && (wallSide != 0 || layerWall || Time.time - lastWallTime < 0.08f);

        GetPlayerInput();

        // 着地の少し前に押したジャンプを、着地した瞬間に出す
        if (Time.time < jumpBufferedUntil && CanGroundJump())
        {
            jumpBufferedUntil = -10f;
            Jump();
        }

        // 上がっている途中でジャンプを離したら、上昇を弱める(小ジャンプ)
        if (jumpRising)
        {
            if (rb.linearVelocity.y <= 0f) jumpRising = false;
            else if (!JumpHeld())
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutMultiplier);
                jumpRising = false;
            }
        }

        UpdateState();
    }

    void GetPlayerInput()
    {
        // CPU はキーボードを読まず、CpuController からの入力を使う
        if (isCpu)
        {
            moveInput = cpuMove;
            if (cpuJumpRequest)
            {
                HandleJump();
                cpuJumpRequest = false;
            }
            return;
        }

        // ソロとオンラインは全員1Pの操作
        if (isSoloMode || IsOnline)
        {
            Handle1PInput();
            return;
        }

        if (playerID == 1) Handle1PInput();
        else Handle2PInput();
    }

    void Handle1PInput()
    {
        moveInput = Input.GetAxisRaw("Horizontal");   // 左スティックもここに入る
        if (Mathf.Abs(moveInput) < 0.3f) moveInput = 0f;
        // ジャンプ:SPACE か ゲームパッドの A
        if (Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.JoystickButton0)) HandleJump();

        // ダッシュ:左 SHIFT か ゲームパッドの B
        KeyCode currentDashKey = (isSoloMode || IsOnline) ? KeyCode.LeftShift : dashKey;
        if ((Input.GetKeyDown(currentDashKey) || Input.GetKeyDown(KeyCode.JoystickButton1)) && moveInput != 0) StartCoroutine(Dash());
    }

    void Handle2PInput()
    {
        float joyInput = Input.GetAxisRaw("Horizontal2");
        float keyInput = 0;
        if (Input.GetKey(KeyCode.LeftArrow)) keyInput = -1;
        if (Input.GetKey(KeyCode.RightArrow)) keyInput = 1;

        moveInput = Mathf.Clamp(joyInput + keyInput, -1f, 1f);

        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Joystick2Button0))
        {
            HandleJump();
        }

        if ((Input.GetKeyDown(dashKey) || Input.GetKeyDown(KeyCode.Joystick2Button1)) && moveInput != 0)
        {
            StartCoroutine(Dash());
        }
    }

    // ジャンプボタンを押し続けているか(CPU はいつも押しっぱなし扱い = 大ジャンプ)
    bool JumpHeld()
    {
        if (isCpu) return true;
        if (!isSoloMode && !IsOnline && playerID == 2)
            return Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.Joystick2Button0);
        return Input.GetButton("Jump") || Input.GetKey(KeyCode.JoystickButton0);
    }

    // 地面にいる(か、落ちてすぐ)で、まだその地面からジャンプしていない
    bool CanGroundJump()
    {
        return !jumpedSinceGrounded && (isGrounded || Time.time - lastGroundedTime <= coyoteTime);
    }

    void HandleJump()
    {
        // 地面(と落ちてすぐ)は通常ジャンプ、空中で壁に触れていれば壁蹴り、それ以外は空中ジャンプ
        if (CanGroundJump()) Jump();
        else if (isTouchingWall) WallJump();
        else if (airJumpsLeft > 0)
        {
            airJumpsLeft--;
            Jump();
        }
        else jumpBufferedUntil = Time.time + jumpBufferTime;   // 着地したら跳ぶ
    }

    void FixedUpdate()
    {
        if (!CanControl) return;

        if (isDashing) return;

        Vector2 v = rb.linearVelocity;

        // 横:いきなり最高速にせず、加速・減速させる(吹き飛び中は操作が効きにくい)
        float target = moveInput * moveSpeed * SpeedMul * externalSpeedMul;
        float accel;
        if (Time.time < knockbackUntil || Time.time < wallJumpLockUntil) accel = airAcceleration * 0.25f;
        else if (isGrounded) accel = Mathf.Abs(target) > 0.01f ? groundAcceleration : groundDeceleration;
        else accel = airAcceleration;
        v.x = Mathf.MoveTowards(v.x, target, accel * Time.fixedDeltaTime);

        // 縦:落ちるときは重力を強く(ふわっとしない)、落ちる速さには上限
        rb.gravityScale = v.y < 0f ? baseGravity * fallGravityMultiplier : baseGravity;
        if (v.y < -maxFallSpeed) v.y = -maxFallSpeed;

        // 壁ずり:壁の方へ押しながら落ちると、ゆっくりずり落ちる
        if (wallSide != 0 && v.y < -wallSlideSpeed && Mathf.Abs(moveInput) > 0.1f && Mathf.Sign(moveInput) == wallSide)
            v.y = -wallSlideSpeed;

        rb.linearVelocity = v;
    }

    void Jump()
    {
        if (!isCpu) SoundManager.Play("jump", 0.6f, 0.05f);
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * JumpMul);
        isGrounded = false;
        jumpedSinceGrounded = true;
        jumpRising = true;
        jumpBufferedUntil = -10f;
    }

    void WallJump()
    {
        // 壁と反対の方へ跳ぶ
        int side = wallSide != 0 ? wallSide : lastWallSide;
        float direction = side != 0 ? -side : ((moveInput != 0) ? -Mathf.Sign(moveInput) : (transform.position.x > 0 ? -1 : 1));
        rb.linearVelocity = new Vector2(direction * wallJumpSideForce, wallJumpForce * JumpMul);
        jumpRising = true;
        wallJumpLockUntil = Time.time + wallJumpLockTime;
        airJumpsLeft = maxAirJumps;   // 壁キックすると、空中ジャンプがもう一度使える
        lastWallTime = -10f;
        if (!isCpu) SoundManager.Play("jump", 0.6f, 0.05f);
    }

    IEnumerator Dash()
    {
        if (Time.time < nextDashTime) yield break;   // 連続ダッシュはできない
        nextDashTime = Time.time + dashTime + dashCooldown;
        isDashing = true;
        jumpRising = false;
        currentState = PlayerState.Dash;
        rb.gravityScale = 0f;
        rb.linearVelocity = new Vector2(Mathf.Sign(moveInput) * dashSpeed, 0f);
        yield return new WaitForSeconds(dashTime);
        rb.gravityScale = baseGravity;
        // ダッシュの終わりは、少し勢いを残す
        rb.linearVelocity = new Vector2(Mathf.Sign(moveInput) * moveSpeed * SpeedMul, 0f);
        isDashing = false;
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        // 踏みつけのバウンドは操作している本人だけが行う
        if (!CanControl) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            if (transform.position.y > collision.transform.position.y + 0.6f)
            {
                string who = isCpu ? "CPU" : (IsOnline ? "Client " + OwnerClientId : "P" + playerID);
                Debug.Log(who + " の踏みつけ成功！");
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * 0.8f);
            }
        }
    }

    void UpdateState()
    {
        if (isDashing) return;
        if (!isGrounded) currentState = PlayerState.Jump;
        else if (Mathf.Abs(moveInput) > 0.1f) currentState = PlayerState.Move;
        else currentState = PlayerState.Idle;
    }
}