using System;
using System.Text;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Meta.XR.MRUtilityKit;

public class HybridNetworkManager : MonoBehaviour
{
    [Header("Server Configuration (Proxmox/Hermes)")]
    public string hermesChatEndpoint = "http://10.252.173.75:8000/api/chat";
    public string sseStreamEndpoint = "http://10.252.173.75:8000/api/stream";

    private ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

    private void Start()
    {
        StartCoroutine(ListenForSSE());
    }

    private void Update()
    {
        while (mainThreadQueue.TryDequeue(out Action action))
        {
            try { action(); }
            catch (Exception e) { Debug.LogError($"[Main Thread Dispatcher] Error: {e.Message}"); }
        }
    }

    [ContextMenu("🚀 TEST KIRIM KE AI (KLIK SINI)")]
    public void TestTriggerAI()
    {
        SendTextPromptToAI("Tolong letakkan kursi di lantai.");
    }

    public void SendTextPromptToAI(string userPrompt)
    {
        StartCoroutine(SendPromptRoutine(userPrompt));
    }

    private IEnumerator SendPromptRoutine(string userPrompt)
    {
        Debug.Log($"<color=cyan>[Hermes AI]</color> Mengirim prompt: {userPrompt}");
        
        string roomContextJson = GetRoomSpatialData();

        var payload = new { 
            prompt = userPrompt,
            spatial_context = JsonConvert.DeserializeObject(roomContextJson) 
        };

        string jsonBody = JsonConvert.SerializeObject(payload);

        using (UnityWebRequest request = new UnityWebRequest(hermesChatEndpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("<color=green>[REST API]</color> Prompt & Data Spasial sukses diterima AI! Menunggu SSE...");
            }
            else
            {
                Debug.LogError($"<color=red>[REST API Error]</color> Gagal kirim ke Hermes: {request.error}");
            }
        }
    }

    // FIX WARNING CS0618: Menggunakan enum SceneLabels bawaan MRUK
    private string GetRoomSpatialData()
    {
        if (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
        {
            return "{\"status\": \"Ruangan belum dipindai atau MRUK belum aktif.\"}";
        }

        var room = MRUK.Instance.GetCurrentRoom();
        
        Bounds bounds = room.GetRoomBounds();
        float width = bounds.size.x;
        float height = bounds.size.y;
        float length = bounds.size.z;
        float volume = width * height * length;

        int wallCount = 0;
        int tableCount = 0;
        
        foreach (var anchor in room.Anchors)
        {
            // Pengecekan Bitmask langsung (Lebih aman dari HasLabel dan bebas warning)
            if (anchor.Label.HasFlag(MRUKAnchor.SceneLabels.WALL_FACE)) wallCount++;
            if (anchor.Label.HasFlag(MRUKAnchor.SceneLabels.TABLE)) tableCount++;
        }

        var spatialData = new {
            room_dimensions = new {
                width_meters = Math.Round(width, 2),
                height_meters = Math.Round(height, 2),
                length_meters = Math.Round(length, 2),
                total_volume_m3 = Math.Round(volume, 2)
            },
            available_surfaces = new {
                walls = wallCount,
                tables = tableCount,
                floor = 1
            }
        };

        return JsonConvert.SerializeObject(spatialData);
    }

    private IEnumerator ListenForSSE()
    {
        using (UnityWebRequest request = new UnityWebRequest(sseStreamEndpoint, "GET"))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "text/event-stream");
            request.certificateHandler = new BypassCertificate();

            request.SendWebRequest();
            Debug.Log("<color=cyan>[SSE Stream]</color> Terhubung ke Server AI. Menunggu instruksi Spasial...");

            int lastPosition = 0;

            while (!request.isDone)
            {
                if (request.downloadHandler.text.Length > lastPosition)
                {
                    string newData = request.downloadHandler.text.Substring(lastPosition);
                    lastPosition = request.downloadHandler.text.Length;

                    string[] lines = newData.Split(new[] { "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("data:"))
                        {
                            string jsonPayload = line.Substring(5).Trim();
                            ParseFastMCP(jsonPayload);
                        }
                    }
                }
                yield return null; 
            }

            yield return new WaitForSeconds(3f);
            StartCoroutine(ListenForSSE());
        }
    }

    private void ParseFastMCP(string jsonText)
    {
        try
        {
            jsonText = jsonText.Trim();
            if (jsonText.StartsWith("```json")) jsonText = jsonText.Substring(7);
            if (jsonText.StartsWith("```")) jsonText = jsonText.Substring(3);
            if (jsonText.EndsWith("```")) jsonText = jsonText.Substring(0, jsonText.Length - 3);
            if (!jsonText.StartsWith("{")) jsonText = "{" + jsonText;
            if (!jsonText.EndsWith("}")) jsonText = jsonText + "}";

            var json = JsonConvert.DeserializeObject<JObject>(jsonText.Trim());
            if (json == null) return;

            string method = json["method"]?.ToString();
            if (method == "tools/call")
            {
                string toolName = json["params"]?["name"]?.ToString();
                var args = json["params"]?["arguments"] as JObject;
                
                mainThreadQueue.Enqueue(() => ExecuteTool(toolName, args));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FastMCP Parse Error] {e.Message}. Raw Text: {jsonText}");
        }
    }

    private void ExecuteTool(string toolName, JObject args)
    {
        Debug.Log($"<color=yellow>[Tool Executed]</color> AI memanggil: {toolName}");

        if (toolName == "spawn_furniture")
        {
            string assetId = args?["asset_id"]?.ToString() ?? "default_cube";
            string surface = args?["surface_label"]?.ToString() ?? "FLOOR";
            
            PerformMRUKSpawn(assetId, surface);
        }
    }

    // FIX ERROR CS1061: Menggunakan Sampling Titik (Aman untuk Semua Versi SDK)
    private void PerformMRUKSpawn(string assetId, string surfaceLabel)
    {
        if (MRUK.Instance == null || MRUK.Instance.GetCurrentRoom() == null)
        {
            Debug.LogError("[MRUK] Room belum terdeteksi. Pastikan Scene Discovery sudah selesai.");
            return;
        }

        var room = MRUK.Instance.GetCurrentRoom();
        
        Transform camTransform = Camera.main != null ? Camera.main.transform : null;
        Vector3 spawnCenter = camTransform != null ? camTransform.position + camTransform.forward * 1.5f : Vector3.zero;

        MRUKAnchor.SceneLabels labelFlag = MRUKAnchor.SceneLabels.FLOOR;
        MRUK.SurfaceType surfaceType = MRUK.SurfaceType.FACING_UP;

        if (surfaceLabel.ToUpper() == "TABLE") { 
            labelFlag = MRUKAnchor.SceneLabels.TABLE; 
        }
        else if (surfaceLabel.ToUpper() == "WALL") { 
            labelFlag = MRUKAnchor.SceneLabels.WALL_FACE; 
            surfaceType = MRUK.SurfaceType.VERTICAL; 
        }
        else if (surfaceLabel.ToUpper() == "CEILING") { 
            labelFlag = MRUKAnchor.SceneLabels.CEILING; 
            surfaceType = MRUK.SurfaceType.FACING_DOWN; 
        }

        bool spawnSuccess = false;
        Vector3 bestPos = Vector3.zero;
        Vector3 bestNorm = Vector3.up;
        float minDistance = float.MaxValue;

        // Sampling 20 titik acak di permukaan, pilih yang paling dekat dengan depan muka pengguna
        for (int i = 0; i < 20; i++)
        {
            if (room.GenerateRandomPositionOnSurface(surfaceType, 0.1f, new LabelFilter(labelFlag), out Vector3 pos, out Vector3 norm))
            {
                float dist = Vector3.Distance(spawnCenter, pos);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestPos = pos;
                    bestNorm = norm;
                    spawnSuccess = true;
                }
            }
        }

        if (spawnSuccess)
        {
            if (AssetRegistryManager.Instance.TryGetPrefab(assetId, out GameObject prefab))
            {
                Quaternion spawnRotation = camTransform != null ? Quaternion.LookRotation(new Vector3(camTransform.forward.x, 0, camTransform.forward.z)) : Quaternion.LookRotation(bestNorm);
                
                GameObject spawnedObj = Instantiate(prefab, bestPos, spawnRotation);
                Debug.Log($"<color=green>[MRUK Success]</color> Objek {assetId} berhasil dimunculkan di depan pengguna pada koordinat {bestPos}");
            }
            else
            {
                Debug.LogError($"[Asset Registry] Aset dengan ID '{assetId}' tidak ditemukan di memori!");
            }
        }
        else
        {
            Debug.LogWarning($"[MRUK Fallback] Gagal menemukan area permukaan yang valid untuk label {labelFlag}");
        }
    }
}

class BypassCertificate : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData) => true;
}