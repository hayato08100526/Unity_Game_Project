using UnityEngine;
using System.Collections;

public class PlayerController : MonoBehaviour
{
    [Header("基本移動")]
    public float moveSpeed = 8f;
    public float jumpForce = 12f;

    [Header("壁蹴り設定")]
    public float wallJumpForce = 10f;
    public float wallJumpSideForce = 12f;
    public LayerMask wallLayer; // Inspectorで「Wall」レイヤーを選択する
    public Transform wallCheck; // 足元や横の判定用

    [Header("ダッシュ設定")]
    public float dashSpeed = 20f;
    public float dashTime = 0.2f;
    private bool isDashing = false;

    public PlayerState currentState = PlayerState.Idle;
    private Rigidbody2D rb;
    private float moveInput;
    private bool isTouchingWall;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (isDashing) return;

        moveInput = Input.GetAxisRaw("Horizontal");

        // 壁に触れているかチェック（半径0.2の円で判定）
        isTouchingWall = Physics2D.OverlapCircle(wallCheck.position, 0.2f, wallLayer);

        if (Input.GetButtonDown("Jump"))
        {
            if (isTouchingWall) WallJump();
            else Jump();
        }

        if (Input.GetKeyDown(KeyCode.LeftShift) && moveInput != 0)
        {
            StartCoroutine(Dash());
        }

        UpdateState();
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
        // 壁と反対方向に飛ばす（現在の入力の逆、または壁の向きの逆）
        float direction = -moveInput;
        if (direction == 0) direction = (transform.position.x > 0) ? -1 : 1;

        rb.linearVelocity = new Vector2(direction * wallJumpSideForce, wallJumpForce);
        Debug.Log("壁蹴り！");
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

    void UpdateState()
    {
        if (isDashing) return;
        if (Mathf.Abs(rb.linearVelocity.y) > 0.1f) currentState = PlayerState.Jump;
        else if (Mathf.Abs(moveInput) > 0.1f) currentState = PlayerState.Move;
        else currentState = PlayerState.Idle;
    }
}