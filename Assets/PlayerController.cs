using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class PlayerController : MonoBehaviour
{
    // --- モード切替フラグ ---
    [Header("モード設定")]
    // staticを外しました。これでインスペクターに表示されます！
    public bool isSoloMode = false;

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

        // 1. プレイヤーごとにデフォルトのダッシュキーを割り当て
        dashKey = (playerID == 1) ? KeyCode.LeftShift : KeyCode.RightShift;

        // 2. タグの確認
        if (!gameObject.CompareTag("Player"))
        {
            Debug.LogWarning(gameObject.name + " のTagを 'Player' に設定してください！");
        }

        // 3. ソロモード設定（念のため2Pを非表示にする処理も残しています）
        if (isSoloMode && playerID == 2)
        {
            gameObject.SetActive(false);
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
        // ソロモードの場合：1Pの入力に従う
        if (isSoloMode)
        {
            Handle1PInput();
            return;
        }

        // 対戦モードの場合：IDごとに分岐
        if (playerID == 1)
        {
            Handle1PInput();
        }
        else
        {
            Handle2PInput();
        }
    }

    void Handle1PInput()
    {
        moveInput = Input.GetAxisRaw("Horizontal");
        if (Input.GetButtonDown("Jump")) HandleJump();

        KeyCode currentDashKey = isSoloMode ? KeyCode.LeftShift : dashKey;
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