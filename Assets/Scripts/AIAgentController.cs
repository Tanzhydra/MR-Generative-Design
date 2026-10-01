using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AIAgentController : MonoBehaviour
{
    private string serverUrl = "http://127.0.0.1:8000/api/command";

    [System.Serializable]
    public class VoiceRequest
    {
        public string text;
    }

    // Fungsi bawaan Unity yang OTOMATIS JALAN saat tombol Play ditekan
    void Start()
    {
        //Debug.Log("Game dimulai! Mengetes tembakan ke AI Lokal...");
        //SendVoiceCommand("Tolong tambahkan meja kayu di lantai");
    }

    public void SendVoiceCommand(string commandText)
    {
        StartCoroutine(PostCommandToServer(commandText));
    }

    IEnumerator PostCommandToServer(string textInput)
    {
        VoiceRequest req = new VoiceRequest { text = textInput };
        string jsonBody = JsonUtility.ToJson(req);

        using (UnityWebRequest request = new UnityWebRequest(serverUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            Debug.Log("Mengirim perintah ke AI Lokal: " + textInput);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("Gagal nyambung ke server: " + request.error);
            }
            else
            {
                string jsonResponse = request.downloadHandler.text;
                Debug.Log("Respon dari AI: " + jsonResponse);
            }
        }
    }
}