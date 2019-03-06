using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LitJson;
using UnityEngine;
using AudioStream;

public class Siri : MonoBehaviour
{
    #region 事件
    void OnEnable()
    {
        DELEGATE.eventStartRefresh += StartRecord;
        DELEGATE.eventEndRefresh += EndRecord;

        aud = this.GetComponent<AudioSource>();
    }


    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            OnBtnStartClick();
        }

        if (Input.GetMouseButtonUp(0))
        {
            OnBtnEndClick();
        }
    }



    void OnDisable()
    {
        DELEGATE.eventStartRefresh -= StartRecord;
        DELEGATE.eventEndRefresh -= EndRecord;
    }


    private void OnBtnStartClick()
    {
        DELEGATE.eventStartRefresh();
    }


    private void OnBtnEndClick()
    {
        DELEGATE.eventEndRefresh();
    }
    #endregion

    #region 录制声音转化为文字

    private string token; //access_token
    private string cuid = "随便写的d"; //用户标识
    private string format = "pcm"; //语音格式
    private int rate = 8000; //采样率
    private int channel = 1; //声道数
    private string speech; //语音数据，进行base64编码
    private int len; //原始语音长度
    private string lan = "zh"; //语种

    private string grant_Type = "client_credentials";
    private string client_ID = "9152186"; //百度appkey
    private string client_Secret = "14c703ce0f900eae40e95b2cdd564472"; //百度Secret Key

    private string baiduAPI = "http://vop.baidu.com/server_api";

    private string getTokenAPIPath =
            "https://aip.baidubce.com/oauth/2.0/token?grant_type=client_credentials&client_id=ekGb1G5XHY4BIVSA8nLzX5cA&client_secret=14c703ce0f900eae40e95b2cdd564472";

    private Byte[] clipByte;

    /// <summary>
    /// 转换出来的TEXT
    /// </summary>
    public static string audioToString;

    private AudioSource aud;
    private int audioLength; //录音的长度

    public void StartRecord()
    {
        Debug.Log("开始说话");

        if (Microphone.devices.Length == 0) return;
        Microphone.End(null);

        aud.clip = Microphone.Start(null, false, 10, rate);
    }

    public void EndRecord()
    {
        Debug.Log("结束说话");

        int lastPos = Microphone.GetPosition(null);
        if (Microphone.IsRecording(null))
            audioLength = lastPos / rate; //录音时长  
        else
            audioLength = 60;
        Microphone.End(null);

        clipByte = GetClipData();
        len = clipByte.Length;
        speech = Convert.ToBase64String(clipByte);
        StartCoroutine(GetToken(getTokenAPIPath));
        StartCoroutine(GetAudioString(baiduAPI));
    }

    /// <summary>
    /// 把录音转换为Byte[]
    /// </summary>
    /// <returns></returns>
    public Byte[] GetClipData()
    {
        if (aud.clip == null)
        {
            Debug.LogError("录音数据为空");
            return null;
        }

        float[] samples = new float[aud.clip.samples];

        aud.clip.GetData(samples, 0);


        Byte[] outData = new byte[samples.Length * 2];

        int rescaleFactor = 32767; //to convert float to Int16   

        for (int i = 0; i < samples.Length; i++)
        {
            short temshort = (short)(samples[i] * rescaleFactor);


            Byte[] temdata = System.BitConverter.GetBytes(temshort);

            outData[i * 2] = temdata[0];
            outData[i * 2 + 1] = temdata[1];
        }
        if (outData == null || outData.Length <= 0)
        {
            Debug.LogError("录音数据为空");
            return null;
        }

        return outData;
    }

    /// <summary>
    /// 获取百度用户令牌
    /// </summary>
    /// <param name="url">获取的url</param>
    /// <returns></returns>
    private IEnumerator GetToken(string url)
    {
        WWW getTW = new WWW(url);
        yield return getTW;
        if (getTW.isDone)
        {
            if (getTW.error == null)
            {
                token = getTW.text;
                StartCoroutine(GetAudioString(baiduAPI));
            }
            else
            {
                Debug.LogError("获取令牌出错" + getTW.error);
            }
        }
        else
        {
            Debug.LogError("下载出错" + getTW.error);
        }
    }

    /// <summary>
    /// 把语音转换为文字
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    private IEnumerator GetAudioString(string url)
    {
        JsonWriter jw = new JsonWriter();
        jw.WriteObjectStart();
        jw.WritePropertyName("format");
        jw.Write(format);
        jw.WritePropertyName("rate");
        jw.Write(rate);
        jw.WritePropertyName("channel");
        jw.Write(channel);
        jw.WritePropertyName("token");
        jw.Write(token);
        jw.WritePropertyName("cuid");
        jw.Write(cuid);
        jw.WritePropertyName("len");
        jw.Write(len);
        jw.WritePropertyName("speech");
        jw.Write(speech);
        jw.WriteObjectEnd();
        WWW getASW = new WWW(url, Encoding.Default.GetBytes(jw.ToString()));

        yield return getASW;
        if (getASW.isDone)
        {

            if (getASW.error == null)
            {
                JsonData getASWJson = JsonMapper.ToObject(getASW.text);
                if (getASWJson["err_msg"].ToString() == "success.")
                {

                    audioToString = getASWJson["result"][0].ToString();
                    if (audioToString.Substring(audioToString.Length - 1) == "，")
                        audioToString = audioToString.Substring(0, audioToString.Length - 1);
                    Debug.Log("说话的问题是:" + audioToString);
                    GetAnswer(audioToString);
                }
                else
                {
                    Debug.LogWarning("没有成功:" + getASWJson["err_msg"].ToString());
                }
            }
            else
            {
                Debug.LogError(getASW.error);
            }
        }
    }


    #endregion

    #region 将文字转出得到回答答案

    private string url = "http://www.tuling123.com/openapi/api?key=d91b25b8866fef13f82cd28c0d523c8a&info=";

    private string QuestionUrl = "http://www.tuling123.com/openapi/api?key=d91b25b8866fef13f82cd28c0d523c8a&info=";

    public string msg = "";

    /// <summary>
    /// 获取图灵返回的答案
    /// </summary>
    /// <param name="msg">提问的问题</param>
    public void GetAnswer(string msg)
    {
        StartCoroutine(GetTuLingtoken(url + msg));
    }

    private string TuLingtoken = "";

    /// <summary>
    /// 图灵的问答系统
    /// </summary>
    /// <param name="Question">要问的问题</param>
    /// <returns></returns>
    private IEnumerator GetTuLingtoken(string url)
    {

        WWW getTW = new WWW(url);

        yield return getTW;
        if (getTW.isDone)
        {
            if (getTW.error == null)
            {
                TuLingtoken = getTW.text;
                TuLingtoken = JsonMapper.ToObject(getTW.text)["text"].ToString();
                //PlayAudio(TuLingtoken);
                Debug.Log(TuLingtoken);
            }
            else
            {
                Debug.LogError(getTW.error);
            }
        }
    }
    #endregion

    #region 开始播放文字答案

    //要播放的语音文字
    public string AudioMsg = "";
    private string urlForward = @"http://tsn.baidu.com/text2audio?tex=";
    public string AudioUrl = "";
    public AudioStreamDemo asDemo;

    public void PlayAudio(string content)
    {
        Debug.Log(content);
        AudioUrl = @"http://tsn.baidu.com/text2audio?tex=" + content + "&lan=zh&cuid=随便写的&ctp=1&tok=" + token;//
        string[] arrPunc = { "，", "。", "”", "；", "“", " " };

        for (int i = 0; i < arrPunc.Length; ++i)
        {
            //用空白字符来替换指定的标点符号，也就相当于删除掉了标点符号
            AudioUrl = AudioUrl.Replace(arrPunc[i], "%20");
        }
        asDemo.SetUrlContext(AudioUrl);
        asDemo.OnPlay();
    }
    #endregion
}
