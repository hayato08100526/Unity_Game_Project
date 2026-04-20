using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    [Header("対戦設定")]
    public int playerID = 1; // 1Pなら1、2Pなら2をInspectorで設定

    [Header("基本移動")]
    public float moveSpeed = 8f;
    public float jumpForce = 12f;

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
    private KeyCode dashKey;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        // プレイヤーごとにダッシュキーを割り当て
        dashKey = (playerID == 1) ? KeyCode.LeftShift : KeyCode.RightShift;

        if (!gameObject.CompareTag("Player"))
        {
            Debug.LogWarning(gameObject.name + " のTagを 'Player' に設定してください！");
        }
    }

    void Update()
    {
        if (isDashing) return;

        GetPlayerInput();

        // 壁検知
        isTouchingWall = Physics2D.OverlapCircle(wallCheck.position, 0.2f, wallLayer);

        UpdateState();
    }

    void GetPlayerInput()
    {
        if (playerID == 1)
        {
            // --- 1P操作: キーボード (Horizontal / Jump) ---
            moveInput = Input.GetAxisRaw("Horizontal");
            if (Input.GetButtonDown("Jump")) HandleJump();
            if (Input.GetKeyDown(dashKey) && moveInput != 0) StartCoroutine(Dash());
        }
        else
        {
            // --- 2P操作: キーボード(矢印) ＋ コントローラー ---

            // スティック入力（さっき作った Horizontal2）
            float joyInput = Input.GetAxisRaw("Horizontal2");

            // キーボード入力
            float keyInput = 0;
            if (Input.GetKey(KeyCode.LeftArrow)) keyInput = -1;
            if (Input.GetKey(KeyCode.RightArrow)) keyInput = 1;

            // 入力を合算（どちらでも動くように）
            moveInput = Mathf.Clamp(joyInput + keyInput, -1f, 1f);

            // ジャンプ: 上矢印 または コントローラーのA/×ボタン(Joystick2Button0)
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Joystick2Button0))
            {
                HandleJump();
            }

            // ダッシュ: 右Shift または コントローラーのB/○ボタン(Joystick2Button1)
            if ((Input.GetKeyDown(dashKey) || Input.GetKeyDown(KeyCode.Joystick2Button1)) && moveInput != 0)
            {
                StartCoroutine(Dash());
            }
        }
    }

    void HandleJump()
    {
        if (isTouchingWall) WallJump();
        else Jump();
    }

    void FixedUpdate()
    {
        if (isDashing) return;
        rb.linearVelocity = new Vector2(moveInput * moveSpeed, rb.linearVelocity.y);
    }

    void Jump()
    {
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
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
        if (collision.gameObject.CompareTag("Player"))
        {
            if (transform.position.y > collision.transform.position.y + 0.6f)
            {
                Debug.Log("P" + playerID + " の勝利！");
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * 0.8f);
            }
        }
    }

    void UpdateState()
    {
        if (isDashing) return;
        if (Mathf.Abs(rb.linearVelocity.y) > 0.1f) currentState = PlayerState.Jump;
        else if (Mathf.Abs(moveInput) > 0.1f) currentState = PlayerState.Move;
        else currentState = PlayerState.Idle;
    }
}