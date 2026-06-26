using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleSTART : MonoBehaviour
{
    public void onClickStartButton()
    {
        SceneManager.LoadScene("SampleScene");
    }
}
