using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleManager : MonoBehaviour
{
    // これが重要！ staticを付けることで、他のスクリプト(GameDirector)から見えるようになります
    public static string GameMode = "Single";

    // 「一人で遊ぶ」ボタンにこれを割り当てる
    public void StartSinglePlayer()
    {
        GameMode = "Single";
        SceneManager.LoadScene("SampleScene");
    }

    // 「対戦する」ボタンにこれを割り当てる
    public void StartVersusPlayer()
    {
        GameMode = "Versus";
        SceneManager.LoadScene("SampleScene");
    }
}