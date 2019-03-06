// (c) 2016, 2017 Martin Cvengros. All rights reserved. Redistribution of source code without permission not allowed.
// uses FMOD Studio by Firelight Technologies

using UnityEngine;
using UnityEngine.Events;

namespace AudioStream
{
    // ========================================================================================================================================
    public static class About
    {
        public static string version = "1.4.1";
    }

    // ========================================================================================================================================
    #region Unity events
    [System.Serializable]
    public class EventWithStringParameter : UnityEvent<string> { };
    [System.Serializable]
    public class EventWithStringBoolParameter : UnityEvent<string, bool> { };
    [System.Serializable]
    public class EventWithStringStringParameter : UnityEvent<string, string> { };
    #endregion

    public enum LogLevel
    {
        ERROR = 0
            , WARNING = 1 << 0
            , INFO = 1 << 1
            , DEBUG = 1 << 2
    }

    public static class AudioStreamSupport
    {
        // ========================================================================================================================================
        #region Logging
        public static void ERRCHECK (
            FMOD.RESULT result
            , LogLevel currentLogLevel
            , string gameObjectName
            , EventWithStringStringParameter onError
            , string customMessage
            , bool throwOnError = true
            )
        {
            if (result != FMOD.RESULT.OK)
            {
                var m = string.Format("{0} {1} - {2}", customMessage, result, FMOD.Error.String(result));

                if (throwOnError)
                    throw new System.Exception(m);
                else
                    LOG(LogLevel.ERROR, currentLogLevel, gameObjectName, onError, m);
            }
            else
            {
                LOG(LogLevel.DEBUG, currentLogLevel, gameObjectName, onError, "{0} {1} - {2}", customMessage, result, FMOD.Error.String(result));
            }
        }

        public static void LOG (
            LogLevel requestedLogLevel
            , LogLevel currentLogLevel
            , string gameObjectName
            , EventWithStringStringParameter onError
            , string format
            , params object[] args
            )
        {
            if (requestedLogLevel == LogLevel.ERROR)
            {
                var msg = string.Format(format, args);

                Debug.LogError(
                    gameObjectName + " " + msg + "\r\n==============================================\r\n"
                    );

                if (onError != null)
                    onError.Invoke(gameObjectName, msg);
            }
            else if (currentLogLevel >= requestedLogLevel)
            {
                if (requestedLogLevel == LogLevel.WARNING)
                    Debug.LogWarningFormat(
                        gameObjectName + " " + format + "\r\n==============================================\r\n"
                        , args);
                else
                    Debug.LogFormat(
                        gameObjectName + " " + format + "\r\n==============================================\r\n"
                        , args);
            }
        }
        #endregion

        // ========================================================================================================================================
        #region audio byte array
        public static int ByteArrayToFloatArray(byte[] byteArray, uint byteArray_length, ref float[] resultFloatArray)
        {
            if (resultFloatArray == null || resultFloatArray.Length != (byteArray_length / 2))
                resultFloatArray = new float[byteArray_length / 2];

            int arrIdx = 0;
            for (int i = 0; i < byteArray_length; i += 2)
                resultFloatArray[arrIdx++] = BytesToFloat(byteArray[i], byteArray[i + 1]);

            return resultFloatArray.Length;
        }

        static float BytesToFloat(byte firstByte, byte secondByte)
        {
            return (float)((short)((int)secondByte << 8 | (int)firstByte)) / 32768f;
        }
        #endregion
    }
}