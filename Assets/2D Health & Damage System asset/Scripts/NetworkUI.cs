using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using TMPro;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private GameObject lobbyCanvas;
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private GameObject startGameButton; // Hostだけに表示
    [SerializeField] private string gameSceneName = "SoloScene";

    private const string ConnectionType = "dtls";
    private bool isBusy = false;

    private void Start()
    {
        if (startGameButton != null) startGameButton.SetActive(false);
    }

    private async Task EnsureSignedInAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private UnityTransport GetTransport()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null) Debug.LogError("UnityTransportが見つかりません。NetworkManagerの設定を確認してください。");
        return transport;
    }

    private void SetButtons(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (clientButton != null) clientButton.interactable = interactable;
    }

    private void SetStatus(string message)
    {
        Debug.Log(message);
        if (joinCodeText != null) joinCodeText.text = message;
    }

    public async void OnHostButton()
    {
        if (isBusy) return;
        isBusy = true;
        SetButtons(false);

        try
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("NetworkManagerがシーンにありません。");
                return;
            }
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("既にネットワークが起動しています。");
                return;
            }

            SetStatus("Relayに接続中...");
            await EnsureSignedInAsync();

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(4);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var transport = GetTransport();
            if (transport == null) return;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));

            if (!NetworkManager.Singleton.StartHost())
            {
                Debug.LogError("StartHostに失敗しました。");
                return;
            }

            SetStatus("Join Code: " + joinCode);
            if (startGameButton != null) startGameButton.SetActive(true);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("Hostの開始に失敗しました");
        }
        finally
        {
            isBusy = false;
            // Host開始に成功した場合はボタンを無効のままにする
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                SetButtons(true);
        }
    }

    // 「ゲーム開始」ボタンに割り当てる(Hostだけが押せる)
    public void OnStartGameButton()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost) return;

        if (nm.SceneManager == null)
        {
            Debug.LogError("SceneManagerがnullです。NetworkManagerの「Enable Scene Management」がオンか確認してください。");
            return;
        }

        nm.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    public async void OnClientButton()
    {
        if (isBusy) return;

        string joinCode = joinCodeInput != null ? joinCodeInput.text.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogWarning("Join Codeが入力されていません。");
            return;
        }

        isBusy = true;
        SetButtons(false);

        try
        {
            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("既にネットワークが起動しています。");
                return;
            }

            SetStatus("接続中...");
            await EnsureSignedInAsync();

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transport = GetTransport();
            if (transport == null) return;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, ConnectionType));

            if (!NetworkManager.Singleton.StartClient())
            {
                Debug.LogError("StartClientに失敗しました。");
                return;
            }

            SetStatus("接続しました。ホストの開始を待っています...");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("接続に失敗しました(Join Codeを確認してください)");
        }
        finally
        {
            isBusy = false;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                SetButtons(true);
        }
    }
}