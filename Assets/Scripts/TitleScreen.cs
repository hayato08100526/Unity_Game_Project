using UnityEngine;
using UnityEngine.SceneManagement;

// タイトル画面(VS CPU / ONLINE MATCH / QUIT)
// MainMenu シーンに空のオブジェクトを作って付ける
public class TitleScreen : MonoBehaviour
{
    [Header("移動先のシーン名")]
    public string cpuSceneName = "CpuBattle";
    public string onlineSceneName = "OnlineLobbyScene";

    [Header("見た目")]
    [Tooltip("Fonts フォルダの Oxanium-Bold など .ttf のフォント。空なら標準のフォント")]
    public Font titleFont;

    private GUIStyle titleStyle;
    private GUIStyle subStyle;
    private GUIStyle buttonStyle;
    private GUIStyle helpStyle;

    private readonly Color bg = new Color32(0x07, 0x09, 0x0D, 0xFF);
    private readonly Color card = new Color32(0x0D, 0x11, 0x17, 0xFF);
    private readonly Color line = new Color32(0x1E, 0x27, 0x33, 0xFF);
    private readonly Color cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
    private readonly Color white = new Color32(0xE6, 0xED, 0xF3, 0xFF);
    private readonly Color grey = new Color32(0x8B, 0x98, 0xA8, 0xFF);

    void Start()
    {
        Time.timeScale = 1f;
    }

    void Update()
    {
        // Enter キーでもすぐ CPU 対戦へ
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            LoadScene(cpuSceneName);
    }

    void OnGUI()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            helpStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };

            if (titleFont != null)
            {
                titleStyle.font = titleFont;
                subStyle.font = titleFont;
                buttonStyle.font = titleFont;
                helpStyle.font = titleFont;
            }
        }

        float unit = Screen.height / 20f;
        titleStyle.fontSize = Mathf.RoundToInt(unit * 2.6f);
        subStyle.fontSize = Mathf.RoundToInt(unit * 0.55f);
        buttonStyle.fontSize = Mathf.RoundToInt(unit * 0.8f);
        helpStyle.fontSize = Mathf.RoundToInt(unit * 0.42f);

        // 背景
        Fill(new Rect(0, 0, Screen.width, Screen.height), bg);

        // タイトル
        titleStyle.normal.textColor = white;
        GUI.Label(new Rect(0, Screen.height * 0.14f, Screen.width, unit * 3f), "B A T T L E   D O O M", titleStyle);

        subStyle.normal.textColor = cyan;
        GUI.Label(new Rect(0, Screen.height * 0.14f + unit * 3f, Screen.width, unit), "R O B O T   V E R S U S   S H O O T E R", subStyle);

        // ボタン
        float w = unit * 9f, h = unit * 1.6f, gap = unit * 0.5f;
        float x = (Screen.width - w) / 2f;
        float y = Screen.height * 0.45f;

        if (Button(new Rect(x, y, w, h), "VS CPU", true)) LoadScene(cpuSceneName);
        if (Button(new Rect(x, y + (h + gap), w, h), "ONLINE MATCH", false)) LoadScene(onlineSceneName);
        if (Button(new Rect(x, y + (h + gap) * 2, w, h), "QUIT", false)) Application.Quit();

        // 操作説明
        helpStyle.normal.textColor = grey;
        string help = "A / D  MOVE     SPACE  JUMP (x2)     MOUSE  AIM     CLICK  SHOOT (HOLD)     1-5 / WHEEL  SWITCH WEAPON";
        GUI.Label(new Rect(unit, Screen.height - unit * 1.8f, Screen.width - unit * 2f, unit * 1.2f), help, helpStyle);
    }

    bool Button(Rect r, string label, bool primary)
    {
        bool hover = r.Contains(Event.current.mousePosition);

        if (primary)
        {
            Fill(r, hover ? white : cyan);
            buttonStyle.normal.textColor = new Color32(0x03, 0x14, 0x17, 0xFF);
        }
        else
        {
            Fill(r, card);
            Border(r, hover ? cyan : line, hover ? 2f : 1f);
            buttonStyle.normal.textColor = hover ? cyan : white;
        }

        GUI.Label(r, label, buttonStyle);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    void LoadScene(string sceneName)
    {
        if (Application.CanStreamedLevelBeLoaded(sceneName))
            SceneManager.LoadScene(sceneName);
        else
            Debug.LogWarning(sceneName + " が Build Profiles の Scene List に入っていません");
    }

    void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    void Border(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }
}