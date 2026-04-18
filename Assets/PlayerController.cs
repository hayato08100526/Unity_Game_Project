using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement; // シーン切り替えを使う場合

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

    // 入力キー管理用
    private KeyCode dashKey;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        // プレイヤーごとにダッシュキーを割り当て
        dashKey = (playerID == 1) ? KeyCode.LeftShift : KeyCode.RightShift;

        // キャラクターに「Player」タグがついているか確認（踏みつけ判定に必要）
        if (!gameObject.CompareTag("Player"))
        {
            Debug.LogWarning(gameObject.name + " に 'Player' タグを設定してください！");
        }
    }

    void Update()
    {
        if (isDashing) return;

        // 1Pと2Pで入力を分岐
        GetPlayerInput();

        // 壁検知
        isTouchingWall = Physics2D.OverlapCircle(wallCheck.position, 0.2f, wallLayer);

        UpdateState();
    }

    void GetPlayerInput()
    {
        if (playerID == 1)
        {
            // 1P操作: WASD + Space
            moveInput = Input.GetAxisRaw("Horizontal");
            if (Input.GetButtonDown("Jump")) HandleJump();
            if (Input.GetKeyDown(dashKey) && moveInput != 0) StartCoroutine(Dash());
        }
        else
        {
            // 2P操作: ArrowKeys + UpArrow
            moveInput = 0;
            if (Input.GetKey(KeyCode.LeftArrow)) moveInput = -1;
            if (Input.GetKey(KeyCode.RightArrow)) moveInput = 1;

            if (Input.GetKeyDown(KeyCode.UpArrow)) HandleJump();
            if (Input.GetKeyDown(dashKey) && moveInput != 0) StartCoroutine(Dash());
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
        // 壁と反対方向に飛ばす
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

    // --- 勝利判定（踏みつけ） ---
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            // 自分の足元が相手の頭（中心）より一定以上高い場合
            if (transform.position.y > collision.transform.position.y + 0.6f)
            {
                Debug.Log("プレイヤー " + playerID + " の勝利！");

                // 勝利した時にやりたい処理（例：少し跳ねる）
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * 0.8f);

                // もし勝利シーンがあるなら呼び出す
                // SceneManager.LoadScene("ResultScene");
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