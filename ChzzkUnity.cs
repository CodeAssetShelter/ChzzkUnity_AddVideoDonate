using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TwitchLib.Api.Helix.Models.Search;
using UnityEngine;
using UnityEngine.Networking;
using WebSocketSharp;

public class ChzzkUnity : MonoBehaviour
{

    public static ChzzkUnity Instance;

    //WSS(WS 말고 WSS) 쓰려면 필요함.
    private enum SslProtocolsHack
    {
        Tls = 192,
        Tls11 = 768,
        Tls12 = 3072
    }


    public enum CHAT_CMD
    {
        PING = 0,
        PONG = 10000,
        CONNECT = 100,
        CONNECTED = 10100,
        REQUEST_RECENT_CHAT = 5101,
        RECENT_CHAT = 15101,
        EVENT = 93006,
        CHAT = 93101,
        DONATION = 93102,
        KICK = 94005,
        BLOCK = 94006,
        BLIND = 94008,
        NOTICE = 94010,
        PENALTY = 94015,
        SEND_CHAT = 3101
    }

    public enum CHAT_TYPE
    {
        TEXT = 1,
        IMAGE = 2,
        STICKER = 3,
        VIDEO = 4,
        RICH = 5,
        DONATION = 10,
        SUBSCRIPTION = 11,
        SYSTEM_MESSAGE = 30
    }

    public string cid;
    public string sid;
    public string token;
    public string my_channel;
    public string uid;

    public string NID_AUT;
    public string NID_SES;

    WebSocket socket = null;
    string wsURL = "wss://kr-ss3.chat.naver.com/chat";
    string wsVideoURL = "wws://ssio08.nchat.naver.com/socket.io";
    float timer = 0f;
    bool running = false;
    
    string heartbeatRequest = "{\"ver\":\"2\",\"cmd\":0}";
    string heartbeatResponse = "{\"ver\":\"2\",\"cmd\":10000}";

    public Action<Profile, string> onMessage = (profile,str) => {};
    public Action<Profile, string, DonationExtras> onDonation = (profile, str, extra) => { };

    private void Awake()
    {
        if (Instance == null)
            Instance = this;                   
    }

    // Start is called before the first frame update
    void Start()
    {
        GetReqGame();
        Connect();
    }

    public void removeAllOnMessageListener() 
    {
        onMessage = (profile, str) => { };
    }

    public void removeAllOnDonationListener()
    {
        onMessage = (profile, str) => { };
    }

    //20초에 한번 HeartBeat 전송해야 함.
    //서버에서 먼저 요청하면 안 해도 됨.
    //TimeScale에 영향 안 받기 위해서 Fixed
    void FixedUpdate()
    {
        if (running)
        {
            timer += Time.unscaledDeltaTime;
            if (timer > 15 && socket.ReadyState == WebSocketState.Open)
            {
                socket.Send(heartbeatRequest);
                timer = 0;
            }
        }
    }
    
    public async Task<ChannelInfo> GetChannelInfo(string channelId)
    {
        string URL = $"https://api.chzzk.naver.com/service/v1/channels/{channelId}";
        UnityWebRequest request = UnityWebRequest.Get(URL);
        await request.SendWebRequest();
        ChannelInfo channelInfo = null;
        Debug.Log(request.downloadHandler.text);
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            channelInfo = JsonUtility.FromJson<ChannelInfo>(request.downloadHandler.text);
        }
        return channelInfo;
    }

    public async Task<LiveStatus> GetLiveStatus(string channelId)
    {
        string URL = $"https://api.chzzk.naver.com/polling/v2/channels/{channelId}/live-status";
        UnityWebRequest request = UnityWebRequest.Get(URL);
        await request.SendWebRequest();
        LiveStatus liveStatus = null;
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            liveStatus = JsonUtility.FromJson<LiveStatus>(request.downloadHandler.text);
            Debug.Log(request.downloadHandler.text);
        }

        return liveStatus;
    }

    public async void GetReqGame()
    {
        string URL = $"https://comm-api.game.naver.com/nng_main/v1/user/getUserStatus";
        using (UnityWebRequest request = UnityWebRequest.Get(URL))
        {
            request.SetRequestHeader("Cookie", string.Format("NID_AUT={0};NID_SES={1}", NID_AUT, NID_SES));
            await request.SendWebRequest();

            GDebug.LogError(request.responseCode);
            if (request.result == UnityWebRequest.Result.Success)
            {
                string parsed = request.downloadHandler.text;
                GDebug.LogError(request.downloadHandler.text);
            }
        }
    }

    public async Task<AccessTokenResult> GetAccessToken(string cid)
    {
        string URL = $"https://comm-api.game.naver.com/nng_main/v1/chats/access-token?channelId={cid}&chatType=STREAMING";
        UnityWebRequest request = UnityWebRequest.Get(URL);
        request.SetRequestHeader("Cookie", string.Format("NID_AUT={0};NID_SES={1}", NID_AUT, NID_SES));
        await request.SendWebRequest();
        AccessTokenResult accessTokenResult = null;
        if (request.result == UnityWebRequest.Result.Success)
        {
            //Cid 획득
            accessTokenResult = JsonUtility.FromJson<AccessTokenResult>(request.downloadHandler.text);
            Debug.Log(request.downloadHandler.text);
        }

        return accessTokenResult;
    }

    public async void Connect()
    {
        if (socket != null && socket.IsAlive)
        {
            socket.Close();
            socket = null;
        }

        LiveStatus liveStatus = await GetLiveStatus(my_channel);
        cid = liveStatus.content.chatChannelId;
        AccessTokenResult accessTokenResult = await GetAccessToken(cid);
        token = accessTokenResult.content.accessToken;
        
        socket = new WebSocket(wsURL);
        //string coo = string.Format("\"NID_AUT\"=\"{0}\";\"NID_SES\"=\"{1}\"", NID_AUT, NID_SES);
        //var cookie = new WebSocketSharp.Net.Cookie("Cookie", coo);
        //socket.SetCookie(cookie);

        //wss라서 ssl protocol을 활성화 해줘야 함.
        var sslProtocolHack = (System.Security.Authentication.SslProtocols)(SslProtocolsHack.Tls12 | SslProtocolsHack.Tls11 | SslProtocolsHack.Tls);
        socket.SslConfiguration.EnabledSslProtocols = sslProtocolHack;

        //이벤트 등록
        socket.OnMessage += Recv;
        socket.OnClose += CloseConnect;
        socket.OnOpen += OnStartChat;

        //연결
        socket.Connect();
    }

    public void Connect(string channelId)
    {
        my_channel = channelId;
        Connect();
    }


    public RecvChatClass monitor;

    void Recv(object sender, MessageEventArgs e)
    {
        //Debug.LogError(e.Data);
        //try
        {                
            IDictionary<string, object> data = JsonConvert.DeserializeObject<IDictionary<string, object>>(e.Data);

            string profileText;
            Profile profile;
            //Cmd에 따라서
            switch ((long)data["cmd"])
            {
                case 0://HeartBeat Request
                    //하트비트 응답해줌.
                    socket.Send(heartbeatResponse);
                    //서버가 먼저 요청해서 응답했으면 타이머 초기화해도 괜찮음.
                    timer = 0;
                    break;
                case 93101://Chat
                    JArray bdy = (JArray)data["bdy"];
                    JObject bdyObject = (JObject)bdy[0];

                    //프로필이.... json이 아니라 string으로 들어옴.
                    //string profileText = bdyObject["profile"].ToString();
                    //profileText = profileText.Replace("\\", "");
                    //Profile profile = JsonUtility.FromJson<Profile>(profileText);

                    //onMessage(profile, bdyObject["msg"].ToString().Trim());
                    //Debug.Log(e.Data);
                    monitor = new(bdyObject);
                    Debug.Log(monitor.bdy.profile.userIdHash);
                    Debug.Log(monitor.bdy.profile.nickname);
                    //if (!string.IsNullOrEmpty(monitor.bdy.msg) && monitor.bdy.profile.userIdHash != my_channel)
                    if (!string.IsNullOrEmpty(monitor.bdy.msg))
                        {
                        switch (monitor.bdy.msg)
                        {
                            case "!연동":
                                AccountSyncManager.Instance.SyncEndPoint(monitor.bdy.profile.userIdHash, monitor.bdy.profile.nickname);
                                break;
                            default:
                                break;
                        }
                    }
                    break;
                case 93102://Donation
                    bdy = (JArray)data["bdy"];
                    bdyObject = (JObject)bdy[0];

                    //프로필 스트링 변환
                    profileText = bdyObject["profile"].ToString();
                    profileText = profileText.Replace("\\", "");
                    profile = JsonUtility.FromJson<Profile>(profileText);

                    //도네이션과 관련된 데이터는 extra
                    string extraText = bdyObject["extra"].ToString();
                    extraText = extraText.Replace("\\", "");
                    DonationExtras extras = JsonUtility.FromJson<DonationExtras>(extraText);


                    onDonation(profile, bdyObject["msg"].ToString(), extras);

                    Debug.LogWarning(e.Data);
                    break;
                case 94008://Blocked Message(CleanBot) 차단된 메세지.
                case 94201://Member Sync 멤버 목록 동기화.
                case 10000://HeartBeat Response 하트비트 응답.
                    break;
                case 10100://Token ACC
                    string mysid = ((JObject)data["bdy"])["sid"].ToString();
                    GDebug.Log(mysid);
                    sid = mysid;
                    break;//Nothing to do
                default:
                    //내가 놓친 cmd가 있나?
                    GDebug.LogError(data["cmd"]);
                    GDebug.LogError(e.Data);
                    break;
            }
        }
        
        //catch (Exception er)
        //{
        //    GDebug.Log(er.ToString());
        //    Debug.LogError(e.Data);
        //}
    }

    void CloseConnect(object sender, CloseEventArgs e)
    {
        GDebug.Log(e.Reason);
        GDebug.Log(e.Code);
        GDebug.Log(e);

        try
        {
            if (socket == null) return;

            if (socket.IsAlive) socket.Close();
        }
        catch (Exception ex)
        {
            Debug.Log(ex.StackTrace);
        }
    }

    void OnStartChat(object sender, EventArgs e)
    {
        Debug.Log($"OPENED : {cid} + {token} + {uid}");

        string message = $"{{\"ver\":\"2\",\"cmd\":100,\"svcid\":\"game\",\"cid\":\"{cid}\",\"bdy\":{{\"uid\":\"{uid}\",\"devType\":2001,\"accTkn\":\"{token}\",\"auth\":\"SEND\"}},\"tid\":1}}";
        //string message = $"{{\"ver\":\"2\",\"cmd\":100,\"svcid\":\"game\",\"cid\":\"{cid}\",\"bdy\":{{\"uid\":null,\"devType\":2001,\"accTkn\":\"{token}\",\"auth\":\"READ\"}},\"tid\":1}}";
        timer = 0;
        running = true;
        socket.Send(message);
    }



    public void StopListening()
    {
        socket.Close();
        socket = null;
    }

    [Serializable]
    public class LiveStatus
    {
        public int code;
        public string message;
        public Content content;

        [Serializable]
        public class Content
        {
            public string liveTitle;
            public string status;
            public int concurrentUserCount;
            public int accumulateCount;
            public bool paidPromotion;
            public bool adult;
            public string chatChannelId;
            public string categoryType;
            public string liveCategory;
            public string liveCategoryValue;
            public string livePollingStatusJson;
            public string faultStatus;
            public string userAdultStatus;
            public bool chatActive;
            public string chatAvailableGroup;
            public string chatAvailableCondition;
            public int minFollowerMinute;
        }
    }

    [Serializable]
    public class AccessTokenResult
    {
        public int code;
        public string message;
        public Content content;
        [Serializable]
        public class Content
        {
            public string accessToken;

            [Serializable]
            public class TemporaryRestrict
            {
                public bool temporaryRestrict;
                public int times;
                public int duration;
                public int createdTime;
            }
            public bool realNameAuth;
            public string extraToken;
        }
    }

    [Serializable]
    public class Profile
    {
        public string userIdHash;
        public string nickname;
        public string profileImageUrl;
        public string userRoleCode;
        public string badge;
        public string title;
        public string verifiedMark;
        public List<String> activityBadges;
        public StreamingProperty streamingProperty;
        [Serializable]
        public class StreamingProperty
        {

        }
    }


    [Serializable]
    public class DonationExtras
    {
        System.Object emojis;
        public bool isAnonymous;
        public string payType;
        public int payAmount;
        public string streamingChannelId;
        public string nickname;
        public string osType;
        public string donationType;

        public List<WeeklyRank> weeklyRankList;
        [Serializable]
        public class WeeklyRank
        {
            public string userIdHash;
            public string nickName;
            public bool verifiedMark;
            public int donationAmount;
            public int ranking;
        }
        public WeeklyRank donationUserWeeklyRank;
    }

    [Serializable]
    public class ChannelInfo
    {
        public int code;
        public string message;
        public Content content;

        [Serializable]
        public class Content
        {
            public string channelId;
            public string channelName;
            public string channelImageUrl;
            public bool verifiedMark;
            public string channelType;
            public string channelDescription;
            public int followerCount;
            public bool openLive;
        }
    }

    [Serializable]
    public class RecvChatClass
    {
        [Serializable]
        public class Body
        {
            public Body(string _msg)
            {
                msg = _msg;
            }
            public string msg;
            public string msgTime;
            public string msgTypeCode;
            public Profile profile;
        }

        public RecvChatClass(JObject j)
        {
            string profileText = j["profile"].ToString();
            profileText = profileText.Replace("\\", "");
            bdy = JsonUtility.FromJson<Body>(j.ToString());
            bdy.profile = JsonUtility.FromJson<Profile>(profileText);
        }

        public Body bdy;
    }

    [Serializable]
    public class SendChatClass
    {
        [Serializable]
        public class Extra
        {
            public string chatType = "STREAMING";
            public string emojis = "";
            public string osType = "PC";
            public string streamingChannelId = "";
        }

        [Serializable]
        public class Body
        {
            public Body(string _msg)
            {
                msg = _msg;
            }
            public string extras = JsonUtility.ToJson(new Extra());
            public string msg;
            public string msgTime = DateTime.Now.ToString();
            public string msgTypeCode = "1";
        }

        public SendChatClass(string _sid, string _cid, string _msg)
        {
            sid = _sid;
            cid = _cid;
            bdy = new Body(_msg);
        }
        public Body bdy;
        public string retry = "false";
        public string cmd = ((int)CHAT_CMD.SEND_CHAT).ToString();
        public string sid;
        public string tid = "3";
        public string cid;
        public string svcid = "game";
        public string ver = "2";
    }

    public void SendChat(string _msg)
    {
        var newChat = new SendChatClass(sid, cid,_msg);
        string msg = JsonUtility.ToJson(newChat, true);
        Debug.Log(msg);
        socket.Send(msg);
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            SendChat("채팅보내요");
        }
    }
}
