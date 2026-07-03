using Unity.Netcode;
using UnityEngine;

public class NetworkConnect : MonoBehaviour
{
    // ホスト（サーバー兼プレイヤー）として開始
    public void StartHost()
    {
        NetworkManager.Singleton.StartHost();
        Debug.Log("Host Started");
    }

    // クライアント（参加者）として開始
    public void StartClient()
    {
        NetworkManager.Singleton.StartClient();
        Debug.Log("Client Started");
    }
}