using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// PC CAM ONVIF HTTP 서버 서비스.
    /// 
    /// 이 서비스는 NVR에서 들어오는 ONVIF SOAP HTTP 요청을 수신하고,
    /// OnvifRequestDispatcher를 통해 SOAP 응답 XML을 생성한 뒤 반환한다.
    /// 
    /// 초기 구현 범위:
    /// - HTTP POST /onvif/device_service
    /// - HTTP POST /onvif/media_service
    /// - GetDeviceInformation
    /// - GetSystemDateAndTime
    /// - GetCapabilities
    /// - GetProfiles
    /// - GetStreamUri
    /// 
    /// HttpListener를 사용하지 않고 TcpListener를 사용하는 이유:
    /// - Windows 7 환경에서 URL ACL 등록 문제가 생길 수 있다.
    /// - 관리자 권한 없이도 단순 TCP 포트 수신 테스트가 가능하다.
    /// </summary>
    public class OnvifHttpServerService : IDisposable
    {
        private readonly object _syncLock = new object();

        private readonly OnvifRequestDispatcher _dispatcher;
        private readonly LogService _logService;

        private TcpListener _listener;
        private Thread _listenerThread;
        private AppConfig _currentConfig;

        private bool _isRunning;
        private bool _disposed;
        private int _listeningPort;

        /// <summary>
        /// ONVIF HTTP 서버 서비스를 생성한다.
        /// </summary>
        /// <param name="dispatcher">ONVIF SOAP 요청 분기 처리기.</param>
        /// <param name="logService">로그 기록 서비스.</param>
        public OnvifHttpServerService(
            OnvifRequestDispatcher dispatcher,
            LogService logService)
        {
            if (dispatcher == null)
                throw new ArgumentNullException("dispatcher");

            if (logService == null)
                throw new ArgumentNullException("logService");

            _dispatcher = dispatcher;
            _logService = logService;
        }

        /// <summary>
        /// 현재 ONVIF HTTP 서버가 실행 중인지 여부를 반환한다.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_syncLock)
                {
                    return _isRunning;
                }
            }
        }

        /// <summary>
        /// 현재 ONVIF HTTP 서버가 수신 중인 포트 번호를 반환한다.
        /// 실행 중이 아니면 0을 반환한다.
        /// </summary>
        public int ListeningPort
        {
            get
            {
                lock (_syncLock)
                {
                    return _isRunning ? _listeningPort : 0;
                }
            }
        }

        /// <summary>
        /// ONVIF HTTP 서버를 시작한다.
        /// 
        /// 현재 2단계 초기 구현에서는 Stream0의 OnvifPort를 사용한다.
        /// 향후 다중 스트림을 ONVIF Profile로 확장할 경우에도
        /// ONVIF 서버 자체는 하나의 포트에서 동작하고 Profile만 여러 개 반환하는 구조가 적합하다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        public void Start(AppConfig config)
        {
            if (_disposed)
                throw new ObjectDisposedException("OnvifHttpServerService");

            if (config == null)
                throw new ArgumentNullException("config");

            lock (_syncLock)
            {
                if (_isRunning)
                    return;

                _currentConfig = config;
                _listeningPort = ResolveOnvifPort(config);

                if (_listeningPort <= 0 || _listeningPort > 65535)
                    throw new InvalidOperationException("ONVIF 포트 값이 올바르지 않습니다. Port=" + _listeningPort);

                _listener = new TcpListener(IPAddress.Any, _listeningPort);
                _listener.Start();

                _isRunning = true;

                _listenerThread = new Thread(AcceptLoop);
                _listenerThread.IsBackground = true;
                _listenerThread.Name = "PC CAM ONVIF HTTP Listener";
                _listenerThread.Start();

                _logService.WriteApp("ONVIF HTTP 서버 시작. Port=" + _listeningPort);
            }
        }

        /// <summary>
        /// ONVIF HTTP 서버를 중지한다.
        /// 
        /// TcpListener.Stop을 호출하면 AcceptTcpClient 대기 중인 스레드에서 예외가 발생할 수 있다.
        /// 이 예외는 정상적인 중지 과정으로 처리한다.
        /// </summary>
        public void Stop()
        {
            lock (_syncLock)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;

                try
                {
                    if (_listener != null)
                        _listener.Stop();
                }
                catch
                {
                }

                _listener = null;

                _logService.WriteApp("ONVIF HTTP 서버 중지 요청");
            }

            try
            {
                if (_listenerThread != null && _listenerThread.IsAlive)
                    _listenerThread.Join(1000);
            }
            catch
            {
            }

            _listenerThread = null;
        }

        /// <summary>
        /// TCP 클라이언트 연결을 대기하고 수락하는 루프.
        /// 
        /// 연결을 수락하면 ThreadPool에서 개별 요청 처리를 수행한다.
        /// </summary>
        private void AcceptLoop()
        {
            while (IsRunning)
            {
                try
                {
                    TcpListener listener = _listener;

                    if (listener == null)
                        return;

                    TcpClient client = listener.AcceptTcpClient();

                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
                catch (SocketException)
                {
                    if (IsRunning)
                        _logService.WriteError("ONVIF HTTP 서버 소켓 오류 발생");
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (IsRunning)
                        _logService.WriteException("ONVIF HTTP 요청 수락 오류", ex);
                }
            }
        }

        /// <summary>
        /// 개별 ONVIF HTTP 클라이언트 요청을 처리한다.
        /// 
        /// 처리 순서:
        /// 1. HTTP 요청 라인과 헤더를 읽는다.
        /// 2. POST 본문 SOAP XML을 읽는다.
        /// 3. 요청 Host 값을 기준으로 NVR이 접근한 PC CAM 주소를 추정한다.
        /// 4. OnvifRequestDispatcher로 SOAP 응답을 생성한다.
        /// 5. HTTP 200 응답으로 SOAP XML을 반환한다.
        /// </summary>
        /// <param name="state">TcpClient 객체.</param>
        private void HandleClient(object state)
        {
            TcpClient client = state as TcpClient;

            if (client == null)
                return;

            try
            {
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 10000;

                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    OnvifHttpRequest request = ReadHttpRequest(stream);

                    if (request == null)
                    {
                        WriteTextResponse(
                            stream,
                            400,
                            "Bad Request",
                            "Invalid ONVIF HTTP request");

                        return;
                    }

                    string host = ResolveRequestHost(request, client);
                    AppConfig config = _currentConfig;

                    if (config == null)
                    {
                        WriteTextResponse(
                            stream,
                            500,
                            "Internal Server Error",
                            "PC CAM config is not loaded");

                        return;
                    }

                    if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteTextResponse(
                            stream,
                            200,
                            "OK",
                            "PC CAM ONVIF service is running");

                        return;
                    }

                    if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteTextResponse(
                            stream,
                            405,
                            "Method Not Allowed",
                            "Only POST is supported for ONVIF SOAP");

                        return;
                    }

                    /*
                     * NVR이 어떤 ONVIF 액션을 호출했는지 로그로 남긴다.
                     * 제조사별 등록 흐름을 분석하기 위해 매우 중요하다.
                     */
                    string actionName = _dispatcher.GetActionName(request.Body);

                    _logService.WriteApp(
                        "ONVIF 요청 수신. Action=" +
                        actionName +
                        ", Path=" +
                        request.Path +
                        ", Host=" +
                        host);

                    if (string.Equals(actionName, "GetProfiles", StringComparison.OrdinalIgnoreCase))
                    {
                        _logService.WriteApp("ONVIF GetProfiles 요청 수신");
                    }

                    if (string.Equals(actionName, "GetStreamUri", StringComparison.OrdinalIgnoreCase))
                    {
                        string profileToken = ExtractProfileTokenForLog(request.Body);

                        _logService.WriteApp(
                            "ONVIF GetStreamUri 요청 수신. ProfileToken=" +
                            profileToken);
                    }

                    if (string.Equals(actionName, "Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        _logService.WriteApp(
                            "ONVIF 미지원 요청 감지. Path=" +
                            request.Path +
                            ", BodyLength=" +
                            (request.Body == null ? 0 : request.Body.Length));
                    }

                    string responseXml = _dispatcher.Dispatch(
                        request.Body,
                        config,
                        host,
                        _listeningPort);

                    if (string.Equals(actionName, "GetStreamUri", StringComparison.OrdinalIgnoreCase))
                    {
                        string rtspUri = ExtractRtspUriForLog(responseXml);

                        _logService.WriteApp(
                            "ONVIF GetStreamUri 응답. RtspUri=" +
                            rtspUri);
                    }

                    WriteSoapResponse(stream, responseXml);
                }
            }
            catch (Exception ex)
            {
                _logService.WriteException("ONVIF HTTP 요청 처리 오류", ex);
            }
        }

        /// <summary>
        /// ONVIF GetStreamUri 요청 XML에서 ProfileToken 값을 로그용으로 추출한다.
        /// 
        /// 지원 형태:
        /// - <ProfileToken>profile_0_main</ProfileToken>
        /// - <trt:ProfileToken>profile_0_sub</trt:ProfileToken>
        /// </summary>
        /// <param name="requestBody">
        /// ONVIF SOAP 요청 본문.
        /// </param>
        /// <returns>
        /// ProfileToken 값.
        /// 찾지 못하면 빈 문자열.
        /// </returns>
        private string ExtractProfileTokenForLog(string requestBody)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return "";

            string elementName = "ProfileToken";

            int nameIndex = requestBody.IndexOf(
                elementName,
                StringComparison.OrdinalIgnoreCase);

            if (nameIndex < 0)
                return "";

            int startCloseIndex = requestBody.IndexOf(
                ">",
                nameIndex,
                StringComparison.OrdinalIgnoreCase);

            if (startCloseIndex < 0)
                return "";

            int valueStartIndex = startCloseIndex + 1;

            int endIndex = requestBody.IndexOf(
                "</",
                valueStartIndex,
                StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0 || endIndex <= valueStartIndex)
                return "";

            return requestBody
                .Substring(valueStartIndex, endIndex - valueStartIndex)
                .Trim();
        }

        /// <summary>
        /// ONVIF GetStreamUri 응답 XML에서 RTSP URI를 로그용으로 추출한다.
        /// </summary>
        /// <param name="responseXml">
        /// ONVIF SOAP 응답 XML.
        /// </param>
        /// <returns>
        /// RTSP URI.
        /// 찾지 못하면 빈 문자열.
        /// </returns>
        private string ExtractRtspUriForLog(string responseXml)
        {
            if (string.IsNullOrWhiteSpace(responseXml))
                return "";

            int startIndex = responseXml.IndexOf(
                "rtsp://",
                StringComparison.OrdinalIgnoreCase);

            if (startIndex < 0)
                return "";

            int endIndex = responseXml.IndexOf(
                "<",
                startIndex,
                StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0 || endIndex <= startIndex)
                return responseXml.Substring(startIndex).Trim();

            return responseXml
                .Substring(startIndex, endIndex - startIndex)
                .Trim();
        }
        /// <summary>
        /// HTTP 요청을 읽어 OnvifHttpRequest 객체로 변환한다.
        /// 
        /// 현재 구현은 Content-Length 기반 요청을 처리한다.
        /// ONVIF NVR의 일반 SOAP POST 요청은 대부분 Content-Length를 포함한다.
        /// </summary>
        /// <param name="stream">클라이언트 네트워크 스트림.</param>
        /// <returns>파싱된 HTTP 요청 객체. 실패 시 null.</returns>
        private OnvifHttpRequest ReadHttpRequest(NetworkStream stream)
        {
            StreamReader reader = new StreamReader(stream, Encoding.UTF8);

            string requestLine = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(requestLine))
                return null;

            string[] firstLineParts = requestLine.Split(' ');

            if (firstLineParts.Length < 2)
                return null;

            OnvifHttpRequest request = new OnvifHttpRequest();
            request.Method = firstLineParts[0];
            request.Path = firstLineParts[1];
            request.Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0)
                    break;

                int colonIndex = line.IndexOf(':');

                if (colonIndex <= 0)
                    continue;

                string name = line.Substring(0, colonIndex).Trim();
                string value = line.Substring(colonIndex + 1).Trim();

                if (!request.Headers.ContainsKey(name))
                    request.Headers.Add(name, value);
                else
                    request.Headers[name] = value;
            }

            int contentLength = 0;

            if (request.Headers.ContainsKey("Content-Length"))
                int.TryParse(request.Headers["Content-Length"], out contentLength);

            if (contentLength > 0)
            {
                char[] buffer = new char[contentLength];
                int totalRead = 0;

                while (totalRead < contentLength)
                {
                    int read = reader.Read(
                        buffer,
                        totalRead,
                        contentLength - totalRead);

                    if (read <= 0)
                        break;

                    totalRead += read;
                }

                request.Body = new string(buffer, 0, totalRead);
            }
            else
            {
                request.Body = "";
            }

            return request;
        }

        /// <summary>
        /// 요청에서 NVR이 접근한 PC CAM 호스트 주소를 추정한다.
        /// 
        /// 우선 HTTP Host 헤더를 사용한다.
        /// Host 헤더가 없으면 TcpClient의 LocalEndPoint 주소를 사용한다.
        /// 이 호스트 값은 GetCapabilities의 XAddr과 GetStreamUri의 RTSP 주소 생성에 사용된다.
        /// </summary>
        /// <param name="request">HTTP 요청 객체.</param>
        /// <param name="client">요청을 보낸 TcpClient.</param>
        /// <returns>NVR이 접근 가능한 PC CAM 호스트 주소.</returns>
        private string ResolveRequestHost(
            OnvifHttpRequest request,
            TcpClient client)
        {
            string hostHeader = "";

            if (request != null &&
                request.Headers != null &&
                request.Headers.ContainsKey("Host"))
            {
                hostHeader = request.Headers["Host"];
            }

            if (!string.IsNullOrWhiteSpace(hostHeader))
            {
                hostHeader = hostHeader.Trim();

                int colonIndex = hostHeader.IndexOf(':');

                if (colonIndex > 0)
                    hostHeader = hostHeader.Substring(0, colonIndex);

                if (!string.IsNullOrWhiteSpace(hostHeader))
                    return hostHeader;
            }

            try
            {
                IPEndPoint localEndPoint = client.Client.LocalEndPoint as IPEndPoint;

                if (localEndPoint != null && localEndPoint.Address != null)
                    return localEndPoint.Address.ToString();
            }
            catch
            {
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// SOAP XML 응답을 HTTP 응답으로 전송한다.
        /// </summary>
        /// <param name="stream">클라이언트 네트워크 스트림.</param>
        /// <param name="xml">전송할 SOAP XML.</param>
        private void WriteSoapResponse(
            NetworkStream stream,
            string xml)
        {
            if (xml == null)
                xml = "";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(xml);

            StringBuilder header = new StringBuilder();
            header.Append("HTTP/1.1 200 OK\r\n");
            header.Append("Content-Type: application/soap+xml; charset=utf-8\r\n");
            header.Append("Content-Length: " + bodyBytes.Length + "\r\n");
            header.Append("Connection: close\r\n");
            header.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());

            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// 일반 텍스트 HTTP 응답을 전송한다.
        /// 
        /// 브라우저로 ONVIF 포트를 열어 서비스 실행 여부를 확인할 때도 사용된다.
        /// </summary>
        /// <param name="stream">클라이언트 네트워크 스트림.</param>
        /// <param name="statusCode">HTTP 상태 코드.</param>
        /// <param name="statusText">HTTP 상태 문구.</param>
        /// <param name="text">응답 본문 텍스트.</param>
        private void WriteTextResponse(
            NetworkStream stream,
            int statusCode,
            string statusText,
            string text)
        {
            if (text == null)
                text = "";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(text);

            StringBuilder header = new StringBuilder();
            header.Append("HTTP/1.1 " + statusCode + " " + statusText + "\r\n");
            header.Append("Content-Type: text/plain; charset=utf-8\r\n");
            header.Append("Content-Length: " + bodyBytes.Length + "\r\n");
            header.Append("Connection: close\r\n");
            header.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());

            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// 설정에서 ONVIF HTTP 포트를 결정한다.
        /// 
        /// 현재는 Stream0의 OnvifPort를 사용한다.
        /// 값이 없거나 잘못된 경우 8080을 기본값으로 사용한다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <returns>ONVIF HTTP 포트.</returns>
        private int ResolveOnvifPort(AppConfig config)
        {
            if (config != null && config.Streams != null)
            {
                foreach (StreamConfig stream in config.Streams)
                {
                    if (stream == null)
                        continue;

                    if (stream.StreamNo == 0 &&
                        stream.OnvifPort > 0 &&
                        stream.OnvifPort <= 65535)
                    {
                        return stream.OnvifPort;
                    }
                }
            }

            return 8080;
        }

        /// <summary>
        /// ONVIF HTTP 서버 리소스를 정리한다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Stop();
            }
            catch
            {
            }

            _disposed = true;
        }

        /// <summary>
        /// 내부 HTTP 요청 모델.
        /// 
        /// ONVIF HTTP 요청의 메서드, 경로, 헤더, 본문을 보관한다.
        /// </summary>
        private class OnvifHttpRequest
        {
            public string Method;
            public string Path;
            public Dictionary<string, string> Headers;
            public string Body;
        }
    }
}