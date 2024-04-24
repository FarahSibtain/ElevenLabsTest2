using System.IO;
using System.Threading.Tasks;
using DoubTech.Elevenlabs.Streaming.NativeWebSocket;
using UnityEditor;

namespace Doubtech.ElevenLabs.Streaming
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;

    public class ElevenLabsWebsocketStreamer : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private int optimizeStreamingLatency = 3; // Default value
        [SerializeField] private string elevenLabsApiKey = "deb1ab0544c1e8aa70035c91d8780c0e"; // Replace with your actual API key

        private const string VOICE_ID = "RCM9nh9o8BS4onDeW9sk";

        private const string URI =
            "wss://api.elevenlabs.io/v1/text-to-speech/{0}/stream-input?model_id=eleven_multilingual_v2&optimize_streaming_latency={1}";



        private WebSocket ws;
        private Queue<string> messageQueue = new Queue<string>();
        private bool isProcessingQueue = false;
        private AudioClip audioClip;
        private MemoryStream audioBuffer;
        private int bufferWriteIndex = 0;
        private bool _initialMessageSent;
        private TaskCompletionSource<bool> _connected;

        async void Start()
        {
            Debug.Log("Start");
            audioBuffer = new MemoryStream();
            // Initialize the audio clip and buffer
            audioClip = AudioClip.Create("ElevenLabsTTS", 44100, 1, 44100, true, PcmReader);

            await ConnectToWebSocket();
            audioSource.clip = audioClip;
            audioSource.Play();
            Debug.Log("Playing audio clip");
        }

        private void PcmReader(float[] data)
        {
            if (null == audioBuffer || audioBuffer.Length == 0) return;
            
            // Create a binary reader for the memory buffer
            using (BinaryReader reader = new BinaryReader(audioBuffer))
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (audioBuffer.Position < audioBuffer.Length)
                    {
                        // Read a 16-bit sample from the memory buffer
                        short sample = reader.ReadInt16();
                        // Convert the sample to a float in the range -1 to 1 and store it in the data array
                        data[i] = sample / 32768f;
                    }
                    else
                    {
                        // If there is no more data in the memory buffer, fill the rest of the data array with zeros
                        data[i] = 0f;
                    }
                }
            }

            if(audioBuffer.Position >= audioBuffer.Length) audioBuffer.SetLength(0);
        }

        async void Update()
        {
            if (null == ws) return;
            if (!audioClip) return;
            
            ws.DispatchMessageQueue();
        }

        async void OnDestroy()
        {
            if (ws != null)
            {
                await ws.Close();
            }
        }

        public void Speak(string message)
        {
            Debug.Log("Speaking ! " + message);
            audioBuffer.SetLength(0);
            // Clear the queue and send the message
            messageQueue.Clear();
            _ = SendMessageToWebSocket(message);
        }

        public void SpeakQueued(string message)
        {
            // Add the message to the queue
            messageQueue.Enqueue(message);
            // Start processing the queue if not already doing so
            if (!isProcessingQueue)
            {
                StartCoroutine(ProcessMessageQueue());
            }
        }

        public void StopPlayback()
        {
            // Stop the audio playback and clear the queue
            audioSource.Stop();
            messageQueue.Clear();
            audioBuffer.SetLength(0);
        }

        public void PausePlayback()
        {
            // Pause the audio playback
            audioSource.Pause();
        }

        VoiceSettings voiceSettings = new VoiceSettings();
        private async Task SendMessageToWebSocket(string message)
        {
            if (ws.State != WebSocketState.Open)
            {
                await ConnectToWebSocket();
            }

            TextToSpeechRequest initialMessage = new TextToSpeechRequest();
            //{
            //    text = " ",
            //    voice_settings = voiceSettings, // new { stability = 0.5, similarity_boost = 0.8 },
            //    xi_api_key = elevenLabsApiKey,
            //    try_trigger_generation = true
            //};


            initialMessage.text = " ";
            initialMessage.model_id = "eleven_multilingual_v2";
            initialMessage.voice_settings = voiceSettings; // new { stability = 0.5, similarity_boost = 0.8 },
            initialMessage.xi_api_key = elevenLabsApiKey;
            // initialMessage.try_trigger_generation = true;

            string initialMessageJson = JsonUtility.ToJson(initialMessage);
            await ws.SendText(initialMessageJson);

            Debug.Log("Sent initial message Json : "+initialMessageJson);
            
            TextToSpeechRequest2 messageData = new TextToSpeechRequest2();
            TextToSpeechRequest2 eosData = new TextToSpeechRequest2();

            eosData.text = "";
            // messageData.text = message;
            messageData.text = "Hello how are you doing today?";
            messageData.try_trigger_generation = true;
            string messageJson = JsonUtility.ToJson(messageData);
            Debug.Log("Sending  message Json : "+messageJson);
            await ws.SendText(messageJson);
            Debug.Log("message Json sent");
            await ws.SendText(JsonUtility.ToJson(eosData));
            Debug.Log("eos Json sent");
        }

        private IEnumerator ProcessMessageQueue()
        {
            isProcessingQueue = true;
            while (messageQueue.Count > 0)
            {
                string message = messageQueue.Dequeue();
                SendMessageToWebSocket(message);
                yield return null;
            }

            isProcessingQueue = false;
        }

        private async Task ConnectToWebSocket()
        {
            Debug.Log("ConnectToWebSocket...");
            _connected = new TaskCompletionSource<bool>();
            string uri = string.Format(URI, VOICE_ID, optimizeStreamingLatency);
            Debug.Log("uri : "+uri);
            ws = new WebSocket(uri);
            ws.OnMessage += (bytes) =>
            {
                
                string message = Encoding.UTF8.GetString(bytes);
                // Debug.Log("OnMessage : "+message);
                var data = JsonUtility.FromJson<MessageData>(message);
                if (data.audio != null)
                {
                    byte[] audioData = System.Convert.FromBase64String(data.audio);
                    audioBuffer.Write(audioData);
                    Debug.Log("Added audio data");
                }
            };
            ws.OnOpen += OnOpen;
            ws.OnMessage += OnMessage;
            ws.OnError += OnError;
            ws.OnClose += OnClose;
            _ = ws.Connect();
            _initialMessageSent = false;
            await _connected.Task;
        }

        private void OnOpen()
        {
            Debug.Log("Connection open!");
            _connected.SetResult(true);
        }
        
        private void OnMessage(byte[] msg)
        {
            Debug.Log("OnMessage! "+msg);            
        }
        

        private void OnError(string errorMsg)
        {
            Debug.Log("OnError! " + errorMsg);
        }
        
        private void OnClose(WebSocketCloseCode code)
        {
            Debug.Log("OnClose! " + code);
            _connected.SetResult(false);
        }
    }

    [System.Serializable]
    class MessageData
    {
        public string audio;
        public bool isFinal;
    }

    [Serializable]
    public class TextToSpeechRequest
    {
        public string text;
        public string model_id; // eleven_monolingual_v1
        public VoiceSettings voice_settings;
        public string xi_api_key;
        public bool try_trigger_generation;
    }

    [Serializable]
    public class TextToSpeechRequest2
    {
        public string text;        
        public bool try_trigger_generation;
    }

    [Serializable]
    public class TextToSpeechRequest3
    {
        public string text;
    }

    [Serializable]
    public class VoiceSettings
    {
        //public float stability = 0.71f; // 0
        //public float similarity_boost = 0.5f; // 0
        //public float style = 0; // 0.5
        //public bool use_speaker_boost = true; // true        

        //new { stability = 0.5, similarity_boost = 0.8 },
        public float stability = 0.5f; // 0
        public float similarity_boost = 0.8f; // 0
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ElevenLabsWebsocketStreamer))]
    public class ElevenLabsWebsocketStreamerEditor : Editor
    {
        private string speakMessage = "";
        private string speakQueuedMessage = "";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ElevenLabsWebsocketStreamer myScript = (ElevenLabsWebsocketStreamer)target;

            speakMessage = EditorGUILayout.TextField("Speak Message", speakMessage);
            if (GUILayout.Button("Speak"))
            {
                myScript.Speak(speakMessage);
            }

            speakQueuedMessage = EditorGUILayout.TextField("Speak Queued Message", speakQueuedMessage);
            if (GUILayout.Button("Speak Queued"))
            {
                myScript.SpeakQueued(speakQueuedMessage);
            }
        }
    }
#endif
}