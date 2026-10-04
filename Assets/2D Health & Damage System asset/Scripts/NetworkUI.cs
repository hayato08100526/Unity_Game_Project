using System;
using System.Collections;
using System.Linq;
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
    [Header("Panels")]
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject roomPanel;

    [Header("Lobby")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private TMP_Text statusText; // ロビー画面のステータス表示(無くても動く)

    [Header("Room - Code")]
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private TMP_Text copyButtonText; // CopyButton の中の文字(無くても動く)

    [Header("Room - Players")]
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private TMP_Text p1TagText;
    [SerializeField] private GameObject p2Row;
    [SerializeField] private TMP_Text p2TagText;
    [SerializeField] private GameObject waitingRow;

    [Header("Room - Start")]
    [SerializeField] private GameObject startGameButton;  // Host だけに表示
    [SerializeField] private GameObject waitingHostText;  // Client だけに表示

    [Header("Settings")]
    [SerializeField] private string gameSceneName = "SoloScene";
    [SerializeField] private int maxPlayers = 2; // Host を含めた人数

    [Header("Colors")]
    [SerializeField] private Color hostColor = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
    [SerializeField] private Color clientColor = new Color32(0xFF, 0xB5, 0x47, 0xFF);
    [SerializeField] private Color readyColor = new Color32(0x6E, 0xE7, 0xA0, 0xFF);

    private const string ConnectionType = "dtls";
    private bool isBusy = false;
    private bool isLeaving = false;
    private string currentJoinCode = "";
    private Coroutine copyRoutine;

    // ───────────── 初期化 ─────────────

    private void Start()
    {
        ShowLobby("");

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnDestroy()
    {
        // NetworkManager はシーンをまたいで残るので、必ず登録を解除する
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // ───────────── 画面切り替え ─────────────

    private void ShowLobby(string message)
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
        if (roomPanel != null) roomPanel.SetActive(false);
        SetButtons(true);
        SetStatus(message);
    }

    private void ShowRoom()
    {
        var nm = NetworkManager.Singleton;
        bool isHost = nm != null && nm.IsHost;

        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (roomPanel != null) roomPanel.SetActive(true);

        if (joinCodeText != null) joinCodeText.text = currentJoinCode;
        if (copyButtonText != null) copyButtonText.text = "COPY CODE";

        // Host は GAME START、Client は WAITING FOR HOST TO START
        if (startGameButton != null) startGameButton.SetActive(isHost);
        if (waitingHostText != null) waitingHostText.SetActive(!isHost);

        // YOU 表示の切り替え
        if (isHost)
        {
            SetTag(p1TagText, "YOU", hostColor);
            SetTag(p2TagText, "READY", readyColor);
        }
        else
        {
            SetTag(p1TagText, "", hostColor);
            SetTag(p2TagText, "YOU", clientColor);
        }

        RefreshPlayers(-1);
    }

    private void SetTag(TMP_Text tag, string text, Color color)
    {
        if (tag == null) return;
        tag.text = text;
        tag.color = color;
    }

    // count に -1 を渡すと自動で数える
    private void RefreshPlayers(int count)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (count < 0)
        {
            if (nm.IsServer) count = nm.ConnectedClientsIds.Count;
            else count = nm.IsConnectedClient ? 2 : 1; // Client が接続済みなら Host と自分で 2 人
        }

        if (playerCountText != null) playerCountText.text = count + " CONNECTED";

        bool hasOpponent = count >= 2;
        if (p2Row != null) p2Row.SetActive(hasOpponent);
        if (waitingRow != null) waitingRow.SetActive(!hasOpponent);

        // 相手がいないときは GAME START を押せないようにする
        if (startGameButton != null)
        {
            var button = startGameButton.GetComponent<Button>();
            if (button != null) button.interactable = hasOpponent;
        }
    }

    // ───────────── 接続イベント ─────────────

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (nm.IsServer)
        {
            RefreshPlayers(-1);
        }
        else if (clientId == nm.LocalClientId)
        {
            // Client:接続が完了したのでルーム画面へ
            ShowRoom();
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (isLeaving) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (nm.IsServer)
        {
            // Host:相手が抜けたので人数を更新
            int count = nm.ConnectedClientsIds.Count;
            if (nm.ConnectedClientsIds.Contains(clientId)) count--;
            RefreshPlayers(count);
            return;
        }

        // Client:Host がいなくなった or 接続に失敗した
        bool wasInRoom = roomPanel != null && roomPanel.activeSelf;
        string message = wasInRoom ? "HOST LEFT THE ROOM" : "CONNECTION FAILED. CHECK THE JOIN CODE.";

        isLeaving = true;
        nm.Shutdown();
        currentJoinCode = "";
        ShowLobby(message);
    }

    // ───────────── 共通処理 ─────────────

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
        if (transport == null) Debug.LogError("UnityTransport not found. Check the NetworkManager settings.");
        return transport;
    }

    private void SetButtons(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (clientButton != null) clientButton.interactable = interactable;
    }

    private void SetStatus(string message)
    {
        if (!string.IsNullOrEmpty(message)) Debug.Log(message);
        if (statusText != null) statusText.text = message;
    }

    private bool CanStartNetwork()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            SetStatus("NETWORK MANAGER NOT FOUND");
            return false;
        }
        if (nm.ShutdownInProgress)
        {
            SetStatus("PLEASE WAIT A MOMENT...");
            return false;
        }
        if (nm.IsListening)
        {
            SetStatus("ALREADY CONNECTED");
            return false;
        }
        return true;
    }

    // ───────────── ボタン ─────────────

    // CREATE ROOM ボタン
    public async void OnHostButton()
    {
        if (isBusy) return;
        if (!CanStartNetwork()) return;

        isBusy = true;
        isLeaving = false;
        SetButtons(false);

        try
        {
            SetStatus("CREATING ROOM...");
            await EnsureSignedInAsync();

            // 引数は Host 以外の人数
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var transport = GetTransport();
            if (transport == null) return;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));

            if (!NetworkManager.Singleton.StartHost())
            {
                SetStatus("FAILED TO START HOST");
                return;
            }

            currentJoinCode = joinCode;
            SetStatus("");
            ShowRoom();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("FAILED TO CREATE ROOM");
        }
        finally
        {
            isBusy = false;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                SetButtons(true);
        }
    }

    // JOIN ボタン
    public async void OnClientButton()
    {
        if (isBusy) return;

        string joinCode = joinCodeInput != null ? joinCodeInput.text.Trim().ToUpper() : "";
        if (string.IsNullOrEmpty(joinCode))
        {
            SetStatus("ENTER A JOIN CODE");
            return;
        }
        if (!CanStartNetwork()) return;

        isBusy = true;
        isLeaving = false;
        SetButtons(false);

        try
        {
            SetStatus("CONNECTING...");
            await EnsureSignedInAsync();

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transport = GetTransport();
            if (transport == null) return;
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, ConnectionType));

            if (!NetworkManager.Singleton.StartClient())
            {
                SetStatus("FAILED TO START CLIENT");
                return;
            }

            // 接続完了は OnClientConnected で受け取ってルーム画面へ
            currentJoinCode = joinCode;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SetStatus("COULD NOT JOIN. CHECK THE JOIN CODE.");
        }
        finally
        {
            isBusy = false;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                SetButtons(true);
        }
    }

    // GAME START ボタン(Host だけが押せる)
    public void OnStartGameButton()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost) return;

        if (nm.SceneManager == null)
        {
            Debug.LogError("SceneManager is null. Turn on 'Enable Scene Management' in NetworkManager.");
            return;
        }

        nm.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    // COPY CODE ボタン
    public void OnCopyButton()
    {
        if (string.IsNullOrEmpty(currentJoinCode)) return;

        GUIUtility.systemCopyBuffer = currentJoinCode;

        if (copyButtonText != null)
        {
            if (copyRoutine != null) StopCoroutine(copyRoutine);
            copyRoutine = StartCoroutine(CopyFeedback());
        }
    }

    private IEnumerator CopyFeedback()
    {
        copyButtonText.text = "COPIED!";
        yield return new WaitForSecondsRealtime(1.5f);
        copyButtonText.text = "COPY CODE";
        copyRoutine = null;
    }

    // LEAVE ROOM ボタン
    public void OnLeaveButton()
    {
        var nm = NetworkManager.Singleton;
        isLeaving = true;

        if (nm != null && nm.IsListening) nm.Shutdown();

        currentJoinCode = "";
        if (joinCodeInput != null) joinCodeInput.text = "";
        ShowLobby("YOU LEFT THE ROOM");
    }
}