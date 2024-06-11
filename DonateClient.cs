using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;
using WebSocketSharp;
using Newtonsoft.Json;
public class DonateClient : MonoBehaviour
{
    public string m_URL_Twip = "wss://io.mytwip.net/socket.io/";
    public string m_Alertbox_Twip = "Insert URL";
    public string m_URL_Toon = "wss://toon.at:8071/";
    public string m_Alertbox_Toon = "Insert URL";

    public WebSocket m_TwipSocket = null;
    public WebSocket m_ToonSocket = null;
    public WebSocket m_ChzzkSocketVideo = null;

    public TextAsset m_TestAsset;


    //private void Start()
    //{
    //    StartCoroutine(GetChzzkDonation());

    //}
    public void ConnectDonateServer()
    {
        //m_CoGetTwip = StartCoroutine(GetTwip());
        StartCoroutine(GetToon());
        //StartCoroutine(GetChzzkDonation());
    }

#if UNITY_EDITOR
    private void FixedUpdate()
    {
        if (Input.GetKeyUp(KeyCode.Space))
        {
            //StartCoroutine(GetTwip());
            var item = JsonConvert.DeserializeObject<ToonDonateData>(m_TestAsset.ToString());
            TwitchClient.instance.GetLoginID(item);
        }
        if (Input.GetKeyDown(KeyCode.V))
        {
            StartCoroutine(GetToon());
        };
    }
#endif

    public class ToonPayload
    {
        public string payload;
    }

    Coroutine m_CoGetToon;
    IEnumerator GetToon()
    {
        using (UnityWebRequest request = UnityWebRequest.Get("https://toon.at/widget/alertbox/" + m_Alertbox_Toon))
        {
            yield return request.SendWebRequest();

            List<string> res = new List<string>();
            GDebug.LogError(request.responseCode);
            if (request.result == UnityWebRequest.Result.Success)
            {
                string parsed = request.downloadHandler.text;
                string start = "\"payload\":\"";
                string end = "\",";
                int start_idx = parsed.IndexOf(start);
                int end_idx = parsed.IndexOf(end, start_idx);
                string payload = parsed.Substring(start_idx, end_idx - start_idx).Trim();
                payload = payload.Replace(start, "");
                payload = payload.Replace(end, "");
                GDebug.LogError(payload);
                GDebug.LogError(parsed);

                m_ToonSocket = new WebSocket(m_URL_Toon + $"{payload}");
                m_ToonSocket.Connect();
                m_ToonSocket.OnMessage += Toon_Recv;
                m_ToonSocket.OnClose += Toon_Close;
                m_ToonSocket.OnOpen += Toon_Open;
                m_ToonSocket.OnError += Toon_Error;


                StartCoroutine(CorToonPing());

                m_CoGetToon = null;
            }
        }
    }


    [Serializable]
    public class ChzzkResponse
    {
        [Serializable]
        public class ChzzkContents
        {
            public string sessionUrl;
        }

        public string code;
        public string message;
        public ChzzkContents content;
    }

    IEnumerator GetChzzkDonation()
    {
        string url = "https://api.chzzk.naver.com/manage/v1/alerts/video@YourSessionURL/session-url";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            List<string> res = new List<string>();
            GDebug.LogError(request.responseCode);
            if (request.result == UnityWebRequest.Result.Success)
            {
                string parsed = request.downloadHandler.text;
                
                
                ChzzkResponse response = JsonUtility.FromJson<ChzzkResponse>(parsed);

                GDebug.LogError(parsed);
                Debug.Log(response.content.sessionUrl);

                // 이게 응답온거
                // https://ssio29.nchat.naver.com:443?
                // auth=AuthKey

                // 이걸로 보내고 싶음
                // ssio09.nchat.naver.com/socket.io/
                // ?auth=AuthKey
                // &EIO=3
                // &transport=websocket
                string socketUrl = 
                    response.content.sessionUrl.Replace("https://", "wss://") +
                    "&EIO=3&transport=websocket";
                socketUrl = socketUrl.Replace(":443", "/socket.io/");

                Debug.LogError(socketUrl);

                m_ChzzkSocketVideo = new WebSocket(socketUrl);
                m_ChzzkSocketVideo.Connect();
                m_ChzzkSocketVideo.OnMessage += Chzzk_Recv;
                m_ChzzkSocketVideo.OnClose += Chzzk_Close;
                m_ChzzkSocketVideo.OnOpen += Chzzk_Open;
                m_ChzzkSocketVideo.OnError += Chzzk_Error;
            }

            var wait = new WaitForSeconds(25f);

            yield return new WaitUntil(() => m_ChzzkSocketVideo.ReadyState == WebSocketState.Open);
            while (true)
            {
                yield return wait;
                if (m_ChzzkSocketVideo != null && m_ChzzkSocketVideo.ReadyState == WebSocketState.Open)
                    m_ChzzkSocketVideo.Send("2");
            }
        }
    }

    public void Chzzk_Recv(object sender, MessageEventArgs e)
    {
        //Debug.Log(e.Data);
        Debug.Log(sender.ToString());
    }
    public void Chzzk_Open(object sender, EventArgs e)
    {
        GDebug.LogError("Open : " + e.ToString());
    }
    public void Chzzk_Close(object sender, CloseEventArgs e)
    {
        GDebug.LogError("Close : " + e.Code);
        GDebug.LogError("Close 2 : " + e.Reason.ToString());
    }
    public void Chzzk_Error(object sender, ErrorEventArgs e)
    {
        GDebug.LogError("Err : " + e.Message);
    }

    Coroutine m_CoGetTwip;
    IEnumerator GetTwip()
    {
        using (UnityWebRequest request = UnityWebRequest.Get("https://twip.kr/widgets/alertbox/YourURL"))
        {
            yield return request.SendWebRequest();

            int key_idx = 0;
            int key_end = 0;
            int key_max = 0;
            string key_res;

            List<string> res = new List<string>();
            GDebug.LogError(request.responseCode);
            if (request.result == UnityWebRequest.Result.Success)
            {
                string parsed = request.downloadHandler.text;

                string token_start = "window.__TOKEN__ = '";
                key_idx = parsed.IndexOf(token_start);
                string token_end = "';</script>";
                key_end = parsed.IndexOf(token_end, key_idx + token_start.Length);
                key_max = key_end - key_idx;
                key_res = parsed.Substring(key_idx + token_start.Length, key_max).Trim().Replace(token_end, "");
                //key_res = key_res.Split('\'')[0];
                res.Add(key_res);

                key_idx = parsed.IndexOf("version: '");
                key_end = parsed.IndexOf("',", key_idx + 10);
                key_max = key_end - key_idx - 2;
                key_res = parsed.Substring(key_idx + 10, key_max);
                key_res = key_res.Split('\'')[0];
                res.Add(key_res);

                //string key = request.downloadHandler.text;
                //string key = IP +
                //            $"?alertbox_key={PORT}" +
                //            $"&version={res[1]}" +
                //            $"&token={res[0]}" +
                //            $"&transport=websocket";

                string key = m_URL_Twip +
                    $"?alertbox_key={m_Alertbox_Twip}" +
                    $"&version={res[1]}" +
                    $"&token=Your Token" +
                    $"&transport=websocket";
                m_TwipSocket = new WebSocket(key);
                m_TwipSocket.Connect();
                m_TwipSocket.OnMessage += Twip_Recv;
                m_TwipSocket.OnClose += Twip_Close;
                m_TwipSocket.OnOpen += Twip_Open;
                m_TwipSocket.OnError += Twip_Error;


                StartCoroutine(CorTwipPing());

                m_CoGetTwip = null;
            }
        }
    }

    IEnumerator CorTwipPing()
    {
        var wait = new WaitForSeconds(22f);
        while (true)
        {
            yield return new WaitForSeconds(1.0f);
            if (m_TwipSocket.ReadyState == WebSocketState.Open)
                break;
        }
        yield return new WaitUntil(() => m_TwipSocket.ReadyState == WebSocketState.Open);
        m_TwipSocket.Send("2");
        while (true)
        {
            yield return wait;
        Debug.Log("SEND TWIP PING");
            m_TwipSocket.Send("2");
        }
    }

    IEnumerator CorToonPing()
    {
        var wait = new WaitForSeconds(12f);
        m_ToonSocket.Ping("#ping");
        while (true)
        {
            yield return wait;
            m_ToonSocket.Ping("#ping");
        }
    }

    public void Twip_Recv(object sender, MessageEventArgs e)
    {
        GDebug.LogError("Recv : " + e.Data.ToString());
        ConvertTwipData(e.Data.ToString());
    }
    public void ConvertTwipData(string _data)
    {
        string res = _data;
        // 외부
        int type_start = res.IndexOf("[");
        int type_end = res.IndexOf(",{");

        // 도네이터 데이터가 아니면 뒤로
        if (type_start == -1 || type_end == -1)
            return;

        var newData = new TwipDonateData();

        string type = res.Substring(type_start + 1, type_end - type_start - 2);

        newData.type = type.Replace("\"","");
        GDebug.Log(newData.type);
        switch (newData.type)
        {
            case ConfigData.TWIP_TYPE_NEW_DONATE:
                break;
            default:
                GameManager_Upgrade.Instance.m_DM.TestLog(_data);
                return;
        }

        // 내부
        int start = res.IndexOf("{");
        int end = res.LastIndexOf("]");
        res = res.Substring(start, end - start);
        newData.value = JsonUtility.FromJson<TwipDonateProperty>(res);

#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(newData.value.watcher_id))
#else
        if (!string.IsNullOrEmpty(newData.value.watcher_id) && !newData.value.repeat)
#endif
            GameManager_Upgrade.Instance.DetectDonation_Twip(newData);
    }

    public void Twip_Open(object sender, EventArgs e)
    {
        GDebug.LogError("Open : " + e.ToString());
    }
    public void Twip_Close(object sender, CloseEventArgs e)
    {
        GDebug.LogError("Close : " + e.Code);
        GDebug.LogError("Close 2 : " + e.Reason.ToString());
        ReconnectTwip();
    }
    public void Twip_Error(object sender, ErrorEventArgs e)
    {
        GDebug.LogError("Err : " + e.Message);
        ReconnectTwip();
    }


    public void Toon_Recv(object sender, MessageEventArgs e)
    {
        GDebug.LogError("Recv : " + e.Data.ToString());
        var item = JsonUtility.FromJson<ToonDonateData>(e.Data.ToString());

        // 도네이터 데이터가 아니면 작동안함
        if (item != null && !string.IsNullOrEmpty(item.content.account))
        {
            GDebug.LogError(item.content.name);
            GDebug.LogError(item.content.account);
            GDebug.LogError(item.replay);
            switch (item.code)
            {
                case "101": // 도네이션
                    break;
                default:
                    GameManager_Upgrade.Instance.m_DM.TestLog(e.Data.ToString());
                    return;
            }

#if UNITY_EDITOR
            TwitchClient.instance.GetLoginID(item);
#else
            if (item.replay != "1")
                TwitchClient.instance.GetLoginID(item);
#endif
        }
    }
    public void Toon_Open(object sender, EventArgs e)
    {
        GDebug.LogError("Open : " + e.ToString());
    }
    public void Toon_Close(object sender, CloseEventArgs e)
    {
        GDebug.LogError("Close : " + e.Code);
        GDebug.LogError("Close 2 : " + e.Reason.ToString());
        ReconnectToon();
    }
    public void Toon_Error(object sender, ErrorEventArgs e)
    {
        GDebug.LogError("Err : " + e.Message);
        ReconnectToon();
    }

    public void ReconnectToon()
    {
        if (m_CoGetToon != null)
            CorDelayAct(
                () =>
                {
                    GameManager_Upgrade.Instance.GameStop(true);
                    GameManager_Upgrade.Instance.m_ToonErr.SetActive(true);
                },
                () =>
                {
                    GameManager_Upgrade.Instance.GameStop(false);
                    GameManager_Upgrade.Instance.m_ToonErr.SetActive(false);
                    m_CoGetToon = StartCoroutine(GetToon());
                }, 3.0f);
    }
    public void ReconnectTwip()
    {
        if (m_CoGetTwip != null)
            CorDelayAct(
                () =>
                {
                    GameManager_Upgrade.Instance.GameStop(true);
                    GameManager_Upgrade.Instance.m_TwipErr.SetActive(true);
                },
                () =>
                {
                    GameManager_Upgrade.Instance.GameStop(false);
                    GameManager_Upgrade.Instance.m_TwipErr.SetActive(false);
                    m_CoGetTwip = StartCoroutine(GetTwip());
                }, 3.0f);
    }

    public IEnumerator CorDelayAct(Action _beforeAct, Action _afterAct, float _sec)
    {
        _beforeAct?.Invoke();
        yield return new WaitForSeconds(_sec);
        _afterAct?.Invoke();
    }
}
