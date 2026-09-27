using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class PlayerController : NetworkBehaviour
{
    [Header("モード設定")]
    public bool isSoloMode = false;

    [Header("対戦設定")]
    public int playerID = 1; // 1Pなら1、2Pなら2をInspectorで設定(ローカル対戦用)

    [Header("基本移動")]
    public float moveSpeed = 8f;
    public float jumpForce = 12f;

    [Header("接地判定")]
    [Tooltip("この角度までの坂を「地面」とみなす")]
    [Range(0f, 89f)] public float maxGroundAngle = 45f;

    [Header("壁蹴り設定")]
    public float wallJumpForce = 10f;
    public float wallJumpSideForce = 12f;
    public LayerMask wallLayer;
    public Transform wallCheck;

    [Header("ダッシュ設定")]
    public float dashSpeed = 20f;
    public float dashTime = 0.2f;
    private bool isDashing = false;

    [Header("状態確認")]
    public PlayerState currentState = PlayerState.Idle;

    private Rigidbody2D rb;
    private float moveInput;
    private bool isTouchingWall;
    private bool isGrounded;
    private ContactFilter2D groundFilter;
    private KeyCode dashKey;

    // オンライン中か / このプレイヤーを操作してよいか
    private bool IsOnline => IsSpawned;
    private bool CanControl => !IsOnline || IsOwner;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

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

        if (isSoloMode && playerID == 2)
        {
            gameObject.SetActive(false);
        }
    }

    void Update()
    {
        // ソロ・ローカル対戦では常に操作可能、オンラインでは自分のプレイヤーだけ
        if (!CanControl) return;

        if (isDashing) return;

        isGrounded = rb.IsTouching(groundFilter);
        isTouchingWall = wallCheck != null &&
                         Physics2D.OverlapCircle(wallCheck.position, 0.2f, wallLayer);

        GetPlayerInput();

        UpdateState();
    }

    void GetPlayerInput()
    {
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
        moveInput = Input.GetAxisRaw("Horizontal");
        if (Input.GetButtonDown("Jump")) HandleJump();

        KeyCode currentDashKey = (isSoloMode || IsOnline) ? KeyCode.LeftShift : dashKey;
        if (Input.GetKeyDown(currentDashKey) && moveInput != 0) StartCoroutine(Dash());
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

    void HandleJump()
    {
        // 地面にいるときは通常ジャンプ、空中で壁に触れていれば壁蹴り
        if (isGrounded) Jump();
        else if (isTouchingWall) WallJump();
    }

    void FixedUpdate()
    {
        if (!CanControl) return;

        if (isDashing) return;
        rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);
    }

    void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        isGrounded = false;
    }

    void WallJump()
    {
        float direction = (moveInput != 0) ? -moveInput : (transform.position.x > 0 ? -1 : 1);
        rb.linearVelocity = new Vector2(direction * wallJumpSideForce, wallJumpForce);
    }

    IEnumerator Dash()
    {
        isDashing = true;
        currentState = PlayerState.Dash;
        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;
        rb.linearVelocity = new Vector2(moveInput * dashSpeed, 0f);
        yield return new WaitForSeconds(dashTime);
        rb.gravityScale = originalGravity;
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
                string who = IsOnline ? "Client " + OwnerClientId : "P" + playerID;
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