using UnityEngine;
using System.Collections;


public delegate void EventStartRefresh();
public delegate void EventEndRefresh();


public class DELEGATE  {

 
    public static EventStartRefresh eventStartRefresh; //开始录音委托
 
    public static EventEndRefresh eventEndRefresh;   //结束录音委托 

}
