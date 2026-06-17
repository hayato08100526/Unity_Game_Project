using UnityEditor;
using UnityEngine;

namespace ThomasDev.HealthSystem
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : MonoBehaviour
    {


        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float maxSpeed = 4f;

        private Rigidbody2D rb;
        private Vector2 input;
        private bool isMoving = false;

        private void Awake() => rb = GetComponent<Rigidbody2D>();

        private void Update()
        {
            input = new Vector2(Input.GetAxisRaw("Horizontal"), 0).normalized;
        }

        private void FixedUpdate()
        {
            if (rb.linearVelocity.x >= maxSpeed) { return; }

            if (input.x != 0)
            {
                rb.linearVelocity += input * moveSpeed * Time.deltaTime;
                isMoving = true;
            } else if(isMoving)
            {
                rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
                isMoving = false;
            }
        }

    }
}