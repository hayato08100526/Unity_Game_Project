using UnityEngine;

// 背景の1枚に付ける。カメラが動いたとき、この背景を少し遅れてついてこさせて奥行きを出す
// follow = 1 ならカメラと一緒に動く(=止まって見える、すごく遠い)、0 なら動かない(=手前)
public class ParallaxLayer : MonoBehaviour
{
    [Tooltip("空なら Main Camera を使う")]
    public Transform cam;

    [Tooltip("カメラにどれだけついていくか。遠い背景ほど 1 に近くする(例:空 0.85 / 工場 0.6)")]
    [Range(0f, 1f)] public float follow = 0.6f;

    [Tooltip("たて方向にもついていくか")]
    public bool followY = true;

    private Vector3 startPos;
    private Vector3 camStart;

    void Start()
    {
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        startPos = transform.position;
        if (cam != null) camStart = cam.position;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        Vector3 d = cam.position - camStart;
        transform.position = new Vector3(
            startPos.x + d.x * follow,
            followY ? startPos.y + d.y * follow : startPos.y,
            startPos.z);
    }
}