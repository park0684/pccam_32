using System;
using System.Text;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// ONVIF SOAP 응답 XML을 생성하는 서비스.
    /// 
    /// 이 클래스는 실제 HTTP 통신을 담당하지 않고,
    /// ONVIF 요청에 대한 SOAP 응답 문자열만 생성한다.
    /// 
    /// 초기 구현 범위:
    /// - GetDeviceInformation
    /// - GetCapabilities
    /// - GetSystemDateAndTime
    /// - GetProfiles
    /// - GetStreamUri
    /// </summary>
    public class OnvifSoapResponseBuilder
    {
        /// <summary>
        /// SOAP Envelope 시작 문자열을 생성한다.
        /// 
        /// ONVIF는 SOAP 기반 XML 메시지를 사용하므로,
        /// 모든 응답은 Envelope 구조 안에 Body를 포함해야 한다.
        /// </summary>
        /// <returns>SOAP Envelope 시작 XML 문자열.</returns>
        private string CreateEnvelopeStart()
        {
            return
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<s:Envelope " +
                "xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" " +
                "xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" " +
                "xmlns:trt=\"http://www.onvif.org/ver10/media/wsdl\" " +
                "xmlns:tt=\"http://www.onvif.org/ver10/schema\">" +
                "<s:Body>";
        }

        /// <summary>
        /// SOAP Envelope 종료 문자열을 생성한다.
        /// </summary>
        /// <returns>SOAP Envelope 종료 XML 문자열.</returns>
        private string CreateEnvelopeEnd()
        {
            return "</s:Body></s:Envelope>";
        }

        /// <summary>
        /// ONVIF 장치 정보 응답을 생성한다.
        /// 
        /// NVR은 장치 등록 과정에서 제조사, 모델명, 펌웨어 버전 등을 조회할 수 있다.
        /// PC CAM은 실제 카메라는 아니지만 ONVIF 장치로 인식되기 위해 기본 장치 정보를 반환한다.
        /// </summary>
        /// <returns>GetDeviceInformation SOAP 응답 XML.</returns>
        public string BuildGetDeviceInformationResponse()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<tds:GetDeviceInformationResponse>");
            sb.Append("<tds:Manufacturer>POSCAM</tds:Manufacturer>");
            sb.Append("<tds:Model>PC CAM</tds:Model>");
            sb.Append("<tds:FirmwareVersion>1.0.0</tds:FirmwareVersion>");
            sb.Append("<tds:SerialNumber>PCCAM-LOCAL</tds:SerialNumber>");
            sb.Append("<tds:HardwareId>PCCAM-32</tds:HardwareId>");
            sb.Append("</tds:GetDeviceInformationResponse>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// ONVIF 장치 시간 정보 응답을 생성한다.
        /// 
        /// 일부 NVR은 장치 등록 과정에서 GetSystemDateAndTime을 호출한다.
        /// 현재 단계에서는 PC의 현재 UTC/Local 시간을 기준으로 응답한다.
        /// </summary>
        /// <returns>GetSystemDateAndTime SOAP 응답 XML.</returns>
        public string BuildGetSystemDateAndTimeResponse()
        {
            DateTime utc = DateTime.UtcNow;
            DateTime local = DateTime.Now;

            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<tds:GetSystemDateAndTimeResponse>");
            sb.Append("<tds:SystemDateAndTime>");
            sb.Append("<tt:DateTimeType>Manual</tt:DateTimeType>");
            sb.Append("<tt:DaylightSavings>false</tt:DaylightSavings>");
            sb.Append("<tt:TimeZone>");
            sb.Append("<tt:TZ>CST-9</tt:TZ>");
            sb.Append("</tt:TimeZone>");

            sb.Append("<tt:UTCDateTime>");
            AppendOnvifDateTime(sb, utc);
            sb.Append("</tt:UTCDateTime>");

            sb.Append("<tt:LocalDateTime>");
            AppendOnvifDateTime(sb, local);
            sb.Append("</tt:LocalDateTime>");

            sb.Append("</tds:SystemDateAndTime>");
            sb.Append("</tds:GetSystemDateAndTimeResponse>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// ONVIF Capability 응답을 생성한다.
        /// 
        /// NVR은 GetCapabilities를 통해 Device Service와 Media Service의 접속 주소를 확인한다.
        /// 여기서는 PC CAM의 ONVIF HTTP 주소를 기반으로 Device/Media XAddr을 반환한다.
        /// </summary>
        /// <param name="onvifHost">NVR이 접근 가능한 PC CAM 호스트 주소.</param>
        /// <param name="onvifPort">ONVIF HTTP 포트.</param>
        /// <returns>GetCapabilities SOAP 응답 XML.</returns>
        public string BuildGetCapabilitiesResponse(string onvifHost, int onvifPort)
        {
            string host = NormalizeHost(onvifHost);
            string baseUrl = "http://" + host + ":" + onvifPort;

            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<tds:GetCapabilitiesResponse>");
            sb.Append("<tds:Capabilities>");

            sb.Append("<tt:Device>");
            sb.Append("<tt:XAddr>" + XmlEscape(baseUrl + "/onvif/device_service") + "</tt:XAddr>");
            sb.Append("</tt:Device>");

            sb.Append("<tt:Media>");
            sb.Append("<tt:XAddr>" + XmlEscape(baseUrl + "/onvif/media_service") + "</tt:XAddr>");
            sb.Append("</tt:Media>");

            sb.Append("</tds:Capabilities>");
            sb.Append("</tds:GetCapabilitiesResponse>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// 기존 호출부 호환용 GetProfiles 응답 생성 메서드.
        /// 
        /// streamNo를 받지 않는 기존 호출은 Stream0 기준으로 처리한다.
        /// </summary>
        /// <param name="config">
        /// 현재 PC CAM 설정.
        /// </param>
        /// <returns>
        /// Stream0 기준 GetProfiles SOAP 응답 XML.
        /// </returns>
        public string BuildGetProfilesResponse(AppConfig config)
        {
            return BuildGetProfilesResponse(config, 0);
        }

        /// <summary>
        /// ONVIF Media Profile 목록 응답을 생성한다.
        /// 
        /// Stream별 ONVIF 포트 구조:
        /// - 8080 → Stream0 Profile만 반환
        /// - 8081 → Stream1 Profile만 반환
        /// - 8082 → Stream2 Profile만 반환
        /// 
        /// 예:
        /// Stream0:
        /// - profile_0_main
        /// - profile_0_sub
        /// 
        /// Stream1:
        /// - profile_1_main
        /// - profile_1_sub
        /// </summary>
        /// <param name="config">
        /// 현재 PC CAM 설정.
        /// </param>
        /// <param name="streamNo">
        /// 현재 ONVIF 포트에 연결된 Stream 번호.
        /// </param>
        /// <returns>
        /// GetProfiles SOAP 응답 XML.
        /// </returns>
        public string BuildGetProfilesResponse(
            AppConfig config,
            int streamNo)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<trt:GetProfilesResponse>");

            bool hasProfile = false;

            StreamConfig stream = FindStream(config, streamNo);

            /*
             * 현재 StreamNo에 해당하는 Stream만 Profile로 반환한다.
             * 기존처럼 전체 config.Streams를 순회하지 않는다.
             */
            if (stream != null && stream.IsEnabled)
            {
                StreamQualityConfig mainStream =
                    stream.MainStream ?? StreamQualityConfig.CreateMain(stream.RtspPath);

                StreamQualityConfig subStream =
                    stream.SubStream ?? StreamQualityConfig.CreateSub(stream.RtspPath + "_sub");

                if (mainStream != null && mainStream.IsEnabled)
                {
                    AppendProfile(
                        sb,
                        stream,
                        mainStream,
                        "main");

                    hasProfile = true;
                }

                if (subStream != null && subStream.IsEnabled)
                {
                    AppendProfile(
                        sb,
                        stream,
                        subStream,
                        "sub");

                    hasProfile = true;
                }
            }

            /*
             * 안전장치:
             * 해당 StreamNo의 Profile이 없으면 해당 StreamNo 기준 Main Profile 하나를 반환한다.
             * 일부 NVR은 GetProfiles 응답이 비어 있으면 장치 등록을 실패 처리할 수 있다.
             */
            if (!hasProfile)
            {
                StreamConfig fallbackStream = CreateFallbackStream(streamNo);

                StreamQualityConfig fallbackMain =
                    fallbackStream.MainStream ?? StreamQualityConfig.CreateMain(fallbackStream.RtspPath);

                AppendProfile(
                    sb,
                    fallbackStream,
                    fallbackMain,
                    "main");
            }

            sb.Append("</trt:GetProfilesResponse>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// ONVIF Stream URI 응답을 생성한다.
        /// 
        /// NVR은 GetProfiles에서 받은 ProfileToken을 기준으로 GetStreamUri를 요청한다.
        /// ProfileToken에 따라 Main/Sub RTSP 주소를 다르게 반환한다.
        /// 
        /// 예:
        /// - profile_0_main → Stream0.MainStream.RtspPath
        /// - profile_0_sub  → Stream0.SubStream.RtspPath
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="rtspHost">NVR이 접근 가능한 PC CAM 호스트 주소.</param>
        /// <param name="profileToken">ONVIF Profile Token.</param>
        /// <returns>GetStreamUri SOAP 응답 XML.</returns>
        public string BuildGetStreamUriResponse(
            AppConfig config,
            string rtspHost,
            string profileToken)
        {
            string rtspUrl = BuildRtspUrl(config, rtspHost, profileToken);

            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<trt:GetStreamUriResponse>");
            sb.Append("<trt:MediaUri>");
            sb.Append("<tt:Uri>" + XmlEscape(rtspUrl) + "</tt:Uri>");
            sb.Append("<tt:InvalidAfterConnect>false</tt:InvalidAfterConnect>");
            sb.Append("<tt:InvalidAfterReboot>false</tt:InvalidAfterReboot>");
            sb.Append("<tt:Timeout>PT60S</tt:Timeout>");
            sb.Append("</trt:MediaUri>");
            sb.Append("</trt:GetStreamUriResponse>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// 지원하지 않는 ONVIF 요청에 대한 SOAP Fault 응답을 생성한다.
        /// 
        /// 초기 구현 범위에 포함되지 않은 요청이 들어온 경우 사용한다.
        /// </summary>
        /// <param name="reason">Fault 사유.</param>
        /// <returns>SOAP Fault XML.</returns>
        public string BuildSoapFault(string reason)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<s:Fault>");
            sb.Append("<s:Code>");
            sb.Append("<s:Value>s:Sender</s:Value>");
            sb.Append("</s:Code>");
            sb.Append("<s:Reason>");
            sb.Append("<s:Text xml:lang=\"ko\">" + XmlEscape(reason) + "</s:Text>");
            sb.Append("</s:Reason>");
            sb.Append("</s:Fault>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// ONVIF DateTime XML 요소를 생성한다.
        /// </summary>
        /// <param name="sb">XML을 추가할 StringBuilder.</param>
        /// <param name="dateTime">출력할 날짜/시간.</param>
        private void AppendOnvifDateTime(StringBuilder sb, DateTime dateTime)
        {
            sb.Append("<tt:Time>");
            sb.Append("<tt:Hour>" + dateTime.Hour + "</tt:Hour>");
            sb.Append("<tt:Minute>" + dateTime.Minute + "</tt:Minute>");
            sb.Append("<tt:Second>" + dateTime.Second + "</tt:Second>");
            sb.Append("</tt:Time>");

            sb.Append("<tt:Date>");
            sb.Append("<tt:Year>" + dateTime.Year + "</tt:Year>");
            sb.Append("<tt:Month>" + dateTime.Month + "</tt:Month>");
            sb.Append("<tt:Day>" + dateTime.Day + "</tt:Day>");
            sb.Append("</tt:Date>");
        }

        /// <summary>
        /// 설정에서 특정 스트림 번호의 StreamConfig를 찾는다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="streamNo">찾을 스트림 번호.</param>
        /// <returns>StreamConfig 객체. 없으면 null.</returns>
        private StreamConfig FindStream(AppConfig config, int streamNo)
        {
            if (config == null || config.Streams == null)
                return null;

            foreach (StreamConfig stream in config.Streams)
            {
                if (stream != null && stream.StreamNo == streamNo)
                    return stream;
            }

            return null;
        }

        /// <summary>
        /// 특정 StreamNo 기준의 fallback StreamConfig를 생성한다.
        /// 
        /// 설정이 없거나 비활성 상태인데도 NVR이 GetProfiles를 요청한 경우,
        /// 빈 응답으로 등록 실패가 발생하지 않도록 최소 Main Profile을 반환하기 위해 사용한다.
        /// </summary>
        /// <param name="streamNo">
        /// Stream 번호.
        /// </param>
        /// <returns>
        /// fallback StreamConfig.
        /// </returns>
        private StreamConfig CreateFallbackStream(int streamNo)
        {
            string rtspPath = GetDefaultRtspPath(streamNo);

            return new StreamConfig
            {
                StreamNo = streamNo,
                IsEnabled = true,
                ScreenName = "PC_CAM_STREAM_" + streamNo,
                Codec = "H264",
                RtspPath = rtspPath,
                Fps = 5,
                Bitrate = "1200k",
                MainStream = StreamQualityConfig.CreateMain(rtspPath),
                SubStream = StreamQualityConfig.CreateSub(rtspPath + "_sub")
            };
        }

        /// <summary>
        /// StreamNo 기준 기본 RTSP 경로를 반환한다.
        /// 
        /// Stream0 → poscam
        /// Stream1 → poscam_1
        /// Stream2 → poscam_2
        /// </summary>
        /// <param name="streamNo">
        /// Stream 번호.
        /// </param>
        /// <returns>
        /// 기본 RTSP 경로.
        /// </returns>
        private string GetDefaultRtspPath(int streamNo)
        {
            if (streamNo <= 0)
                return "poscam";

            return "poscam_" + streamNo;
        }


        /// <summary>
        /// RTSP Read 주소를 생성한다.
        /// 
        /// ProfileToken에 따라 MainStream 또는 SubStream의 RtspPath를 반환한다.
        /// 
        /// 예:
        /// profile_0_main → Stream0.MainStream.RtspPath
        /// profile_0_sub  → Stream0.SubStream.RtspPath
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="host">NVR이 접근 가능한 PC CAM 호스트.</param>
        /// <param name="profileToken">ONVIF Profile Token.</param>
        /// <returns>RTSP URL.</returns>
        private string BuildRtspUrl(AppConfig config, string host, string profileToken)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (config.RtspServer == null)
                throw new InvalidOperationException("RTSP 서버 설정이 없습니다.");

            if (string.IsNullOrWhiteSpace(profileToken))
                profileToken = "profile_0_main";

            int streamNo = ParseStreamNoFromProfileToken(profileToken);
            bool isSub = IsSubProfileToken(profileToken);

            StreamConfig stream = FindStream(config, streamNo);

            if (stream == null)
                throw new InvalidOperationException("스트림 설정을 찾을 수 없습니다. StreamNo=" + streamNo);

            StreamQualityConfig quality;

            if (isSub)
                quality = stream.SubStream ?? StreamQualityConfig.CreateSub(stream.RtspPath + "_sub");
            else
                quality = stream.MainStream ?? StreamQualityConfig.CreateMain(stream.RtspPath);

            string rtspHost = NormalizeHost(host);

            string path = NormalizeRtspPath(
                quality == null
                    ? stream.RtspPath
                    : quality.RtspPath);

            string userId = "";
            string password = "";

            if (config.Onvif != null)
            {
                userId = config.Onvif.UserId ?? "";
                password = config.Onvif.Password ?? "";
            }

            if (!string.IsNullOrWhiteSpace(userId) &&
                !string.IsNullOrWhiteSpace(password))
            {
                return
                    "rtsp://" +
                    Uri.EscapeDataString(userId) +
                    ":" +
                    Uri.EscapeDataString(password) +
                    "@" +
                    rtspHost +
                    ":" +
                    config.RtspServer.RtspPort +
                    "/" +
                    path;
            }

            return
                "rtsp://" +
                rtspHost +
                ":" +
                config.RtspServer.RtspPort +
                "/" +
                path;
        }

        /// <summary>
        /// RTSP 경로 앞뒤의 슬래시를 제거한다.
        /// </summary>
        /// <param name="path">정리할 RTSP 경로.</param>
        /// <returns>정리된 RTSP 경로.</returns>
        private string NormalizeRtspPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "poscam";

            path = path.Trim();

            while (path.StartsWith("/"))
                path = path.Substring(1);

            while (path.EndsWith("/"))
                path = path.Substring(0, path.Length - 1);

            if (string.IsNullOrWhiteSpace(path))
                return "poscam";

            return path;
        }

        /// <summary>
        /// 호스트 값이 비어 있으면 127.0.0.1로 보정한다.
        /// </summary>
        /// <param name="host">입력 호스트.</param>
        /// <returns>보정된 호스트.</returns>
        private string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return "127.0.0.1";

            return host.Trim();
        }

        /// <summary>
        /// 설정 코덱 값을 ONVIF Encoding 값으로 변환한다.
        /// </summary>
        /// <param name="codec">설정 코덱 값.</param>
        /// <returns>ONVIF Encoding 값.</returns>
        private string NormalizeOnvifCodec(string codec)
        {
            if (string.Equals(codec, "H265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "H.265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "HEVC", StringComparison.OrdinalIgnoreCase))
            {
                return "H265";
            }

            return "H264";
        }

        /// <summary>
        /// XML 특수문자를 안전하게 이스케이프한다.
        /// </summary>
        /// <param name="value">원본 문자열.</param>
        /// <returns>XML 이스케이프 처리된 문자열.</returns>
        private string XmlEscape(string value)
        {
            if (value == null)
                return "";

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        /// <summary>
        /// ONVIF Service 목록 응답을 생성한다.
        /// 
        /// 일부 NVR은 GetCapabilities 대신 또는 그 전에 GetServices를 호출하여
        /// Device Service와 Media Service의 XAddr 주소를 확인한다.
        /// 
        /// 이 응답에는 현재 PC CAM이 제공하는 최소 ONVIF 서비스인
        /// Device Service와 Media Service를 포함한다.
        /// </summary>
        /// <param name="onvifHost">
        /// NVR이 접근 가능한 PC CAM 호스트 주소.
        /// </param>
        /// <param name="onvifPort">
        /// ONVIF HTTP 서버 포트.
        /// </param>
        /// <returns>
        /// GetServices SOAP 응답 XML.
        /// </returns>
        public string BuildGetServicesResponse(string onvifHost, int onvifPort)
        {
            string host = NormalizeHost(onvifHost);
            string baseUrl = "http://" + host + ":" + onvifPort;

            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<tds:GetServicesResponse>");

            sb.Append("<tds:Service>");
            sb.Append("<tds:Namespace>http://www.onvif.org/ver10/device/wsdl</tds:Namespace>");
            sb.Append("<tds:XAddr>" + XmlEscape(baseUrl + "/onvif/device_service") + "</tds:XAddr>");
            sb.Append("<tds:Version>");
            sb.Append("<tt:Major>2</tt:Major>");
            sb.Append("<tt:Minor>0</tt:Minor>");
            sb.Append("</tds:Version>");
            sb.Append("</tds:Service>");

            sb.Append("<tds:Service>");
            sb.Append("<tds:Namespace>http://www.onvif.org/ver10/media/wsdl</tds:Namespace>");
            sb.Append("<tds:XAddr>" + XmlEscape(baseUrl + "/onvif/media_service") + "</tds:XAddr>");
            sb.Append("<tds:Version>");
            sb.Append("<tt:Major>2</tt:Major>");
            sb.Append("<tt:Minor>0</tt:Minor>");
            sb.Append("</tds:Version>");
            sb.Append("</tds:Service>");

            sb.Append("</tds:GetServicesResponse>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// ONVIF Scope 목록 응답을 생성한다.
        /// 
        /// NVR은 GetScopes 응답을 통해 장치 이름, 위치, 타입 같은 식별 정보를 확인할 수 있다.
        /// 자동 검색 또는 수동 등록 과정에서 장치 표시명으로 사용될 수 있다.
        /// </summary>
        /// <returns>
        /// GetScopes SOAP 응답 XML.
        /// </returns>
        public string BuildGetScopesResponse()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<tds:GetScopesResponse>");

            sb.Append("<tds:Scopes>");
            sb.Append("<tt:ScopeDef>Fixed</tt:ScopeDef>");
            sb.Append("<tt:ScopeItem>onvif://www.onvif.org/type/video_encoder</tt:ScopeItem>");
            sb.Append("</tds:Scopes>");

            sb.Append("<tds:Scopes>");
            sb.Append("<tt:ScopeDef>Fixed</tt:ScopeDef>");
            sb.Append("<tt:ScopeItem>onvif://www.onvif.org/name/PC_CAM</tt:ScopeItem>");
            sb.Append("</tds:Scopes>");

            sb.Append("<tds:Scopes>");
            sb.Append("<tt:ScopeDef>Fixed</tt:ScopeDef>");
            sb.Append("<tt:ScopeItem>onvif://www.onvif.org/location/POS</tt:ScopeItem>");
            sb.Append("</tds:Scopes>");

            sb.Append("</tds:GetScopesResponse>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// ONVIF Profile XML을 추가한다.
        /// </summary>
        /// <param name="sb">XML 문자열 작성기.</param>
        /// <param name="stream">상위 Stream 설정.</param>
        /// <param name="quality">Main 또는 Sub 품질 설정.</param>
        /// <param name="qualityName">main 또는 sub.</param>
        private void AppendProfile(
            StringBuilder sb,
            StreamConfig stream,
            StreamQualityConfig quality,
            string qualityName)
        {
            if (stream == null)
                return;

            if (quality == null)
                return;

            string normalizedQualityName =
                string.Equals(qualityName, "sub", StringComparison.OrdinalIgnoreCase)
                    ? "sub"
                    : "main";

            string profileToken =
                BuildProfileToken(stream.StreamNo, normalizedQualityName);

            string profileName =
                BuildProfileName(stream, normalizedQualityName);

            int width = quality.Width;
            int height = quality.Height;

            /*
             * MainStream은 Width/Height가 0일 수 있다.
             * ONVIF Profile 응답에는 해상도 값이 필요하므로 기본값을 사용한다.
             */
            if (width <= 0)
                width = 1920;

            if (height <= 0)
                height = 1080;

            int fps = quality.Fps > 0
                ? quality.Fps
                : stream.Fps;

            if (fps <= 0)
                fps = 5;

            int bitrateLimit = ParseBitrateKbps(quality.Bitrate);

            if (bitrateLimit <= 0)
                bitrateLimit = ParseBitrateKbps(stream.Bitrate);

            if (bitrateLimit <= 0)
                bitrateLimit = 1200;

            string codec = NormalizeOnvifCodec(stream.Codec);

            string sourceToken = "source_" + stream.StreamNo;
            string encoderToken = "video_encoder_" + stream.StreamNo + "_" + normalizedQualityName;

            sb.Append("<trt:Profiles token=\"" + XmlEscape(profileToken) + "\" fixed=\"true\">");
            sb.Append("<tt:Name>" + XmlEscape(profileName) + "</tt:Name>");

            sb.Append("<tt:VideoSourceConfiguration token=\"video_source_" + stream.StreamNo + "\">");
            sb.Append("<tt:Name>VideoSource" + stream.StreamNo + "</tt:Name>");
            sb.Append("<tt:UseCount>1</tt:UseCount>");
            sb.Append("<tt:SourceToken>" + XmlEscape(sourceToken) + "</tt:SourceToken>");
            sb.Append("<tt:Bounds x=\"0\" y=\"0\" width=\"" + width + "\" height=\"" + height + "\" />");
            sb.Append("</tt:VideoSourceConfiguration>");

            sb.Append("<tt:VideoEncoderConfiguration token=\"" + XmlEscape(encoderToken) + "\">");
            sb.Append("<tt:Name>VideoEncoder" + stream.StreamNo + "_" + normalizedQualityName + "</tt:Name>");
            sb.Append("<tt:UseCount>1</tt:UseCount>");
            sb.Append("<tt:Encoding>" + codec + "</tt:Encoding>");
            sb.Append("<tt:Resolution>");
            sb.Append("<tt:Width>" + width + "</tt:Width>");
            sb.Append("<tt:Height>" + height + "</tt:Height>");
            sb.Append("</tt:Resolution>");
            sb.Append("<tt:Quality>5</tt:Quality>");
            sb.Append("<tt:RateControl>");
            sb.Append("<tt:FrameRateLimit>" + fps + "</tt:FrameRateLimit>");
            sb.Append("<tt:EncodingInterval>1</tt:EncodingInterval>");
            sb.Append("<tt:BitrateLimit>" + bitrateLimit + "</tt:BitrateLimit>");
            sb.Append("</tt:RateControl>");
            sb.Append("</tt:VideoEncoderConfiguration>");

            sb.Append("</trt:Profiles>");
        }

        /// <summary>
        /// ONVIF ProfileToken을 생성한다.
        /// </summary>
        /// <param name="streamNo">스트림 번호.</param>
        /// <param name="qualityName">main 또는 sub.</param>
        /// <returns>ProfileToken 문자열.</returns>
        private string BuildProfileToken(int streamNo, string qualityName)
        {
            if (string.IsNullOrWhiteSpace(qualityName))
                qualityName = "main";

            return "profile_" + streamNo + "_" + qualityName.ToLower();
        }

        /// <summary>
        /// Profile 표시명을 생성한다.
        /// </summary>
        /// <param name="stream">Stream 설정.</param>
        /// <param name="qualityName">main 또는 sub.</param>
        /// <returns>Profile 이름.</returns>
        private string BuildProfileName(StreamConfig stream, string qualityName)
        {
            string baseName = "";

            if (stream != null && !string.IsNullOrWhiteSpace(stream.ScreenName))
                baseName = stream.ScreenName;
            else if (stream != null)
                baseName = "PC_CAM_STREAM_" + stream.StreamNo;
            else
                baseName = "PC_CAM_STREAM_0";

            if (string.Equals(qualityName, "sub", StringComparison.OrdinalIgnoreCase))
                return baseName + "_SUB";

            return baseName + "_MAIN";
        }

        /// <summary>
        /// ProfileToken에서 스트림 번호를 추출한다.
        /// 
        /// 예상 형식:
        /// profile_0_main
        /// profile_0_sub
        /// </summary>
        private int ParseStreamNoFromProfileToken(string profileToken)
        {
            if (string.IsNullOrWhiteSpace(profileToken))
                return 0;

            string[] parts = profileToken.Split('_');

            if (parts.Length < 3)
                return 0;

            int streamNo;

            if (int.TryParse(parts[1], out streamNo))
                return streamNo;

            return 0;
        }

        /// <summary>
        /// VideoEncoderConfiguration token에서 StreamNo를 추출한다.
        /// 
        /// 예상 형식:
        /// - video_encoder_0_main
        /// - video_encoder_0_sub
        /// 
        /// 토큰 형식이 맞지 않으면 fallbackStreamNo를 반환한다.
        /// </summary>
        /// <param name="configurationToken">VideoEncoderConfiguration token.</param>
        /// <param name="fallbackStreamNo">추출 실패 시 사용할 기본 StreamNo.</param>
        /// <returns>StreamNo.</returns>
        private int ParseStreamNoFromConfigurationToken(
            string configurationToken,
            int fallbackStreamNo)
        {
            if (string.IsNullOrWhiteSpace(configurationToken))
                return fallbackStreamNo;

            string[] parts = configurationToken.Split('_');

            /*
             * video_encoder_0_main
             * [0] video
             * [1] encoder
             * [2] 0
             * [3] main
             */
            if (parts.Length >= 3)
            {
                int streamNo;

                if (int.TryParse(parts[2], out streamNo))
                    return streamNo;
            }

            return fallbackStreamNo;
        }

        /// <summary>
        /// VideoEncoderConfiguration token이 SubStream을 가리키는지 확인한다.
        /// </summary>
        /// <param name="configurationToken">VideoEncoderConfiguration token.</param>
        /// <returns>SubStream token이면 true.</returns>
        private bool IsSubConfigurationToken(string configurationToken)
        {
            if (string.IsNullOrWhiteSpace(configurationToken))
                return false;

            return configurationToken.IndexOf("_sub", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// ProfileToken이 SubStream을 가리키는지 확인한다.
        /// </summary>
        private bool IsSubProfileToken(string profileToken)
        {
            if (string.IsNullOrWhiteSpace(profileToken))
                return false;

            return profileToken.IndexOf("_sub", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Bitrate 문자열을 kbps 숫자로 변환한다.
        /// 
        /// 예:
        /// 800k → 800
        /// 1200k → 1200
        /// 2m → 2000
        /// </summary>
        private int ParseBitrateKbps(string bitrate)
        {
            if (string.IsNullOrWhiteSpace(bitrate))
                return 0;

            string value = bitrate.Trim().ToLower();

            try
            {
                if (value.EndsWith("k"))
                {
                    value = value.Substring(0, value.Length - 1);
                    int kbps;

                    if (int.TryParse(value, out kbps))
                        return kbps;
                }

                if (value.EndsWith("m"))
                {
                    value = value.Substring(0, value.Length - 1);
                    int mbps;

                    if (int.TryParse(value, out mbps))
                        return mbps * 1000;
                }

                int raw;

                if (int.TryParse(value, out raw))
                    return raw;
            }
            catch
            {
            }

            return 0;
        }

        /// <summary>
        /// ONVIF GetVideoEncoderConfigurationOptions 요청에 대한 응답 XML을 생성한다.
        /// 
        /// NVR은 장비 등록 과정에서 VideoEncoderConfiguration token을 기준으로
        /// 해당 스트림이 지원하는 해상도, FPS, GOP, H.264 Profile 범위를 조회한다.
        /// 
        /// PC CAM은 ONVIF로 설정 변경을 받는 장비가 아니라 읽기 전용 가상 카메라에 가깝기 때문에,
        /// 현재 설정값을 기준으로 최소 호환 가능한 옵션 범위를 반환한다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="configurationToken">NVR이 요청한 VideoEncoderConfiguration token.</param>
        /// <param name="fallbackStreamNo">토큰에서 StreamNo를 찾지 못할 때 사용할 기본 StreamNo.</param>
        /// <returns>GetVideoEncoderConfigurationOptions SOAP 응답 XML.</returns>
        public string BuildGetVideoEncoderConfigurationOptionsResponse(
            AppConfig config,
            string configurationToken,
            int fallbackStreamNo)
        {
            int streamNo = ParseStreamNoFromConfigurationToken(configurationToken, fallbackStreamNo);
            bool isSub = IsSubConfigurationToken(configurationToken);

            StreamConfig stream = FindStream(config, streamNo);

            if (stream == null)
                stream = CreateFallbackStream(streamNo);

            StreamQualityConfig quality;

            if (isSub)
                quality = stream.SubStream ?? StreamQualityConfig.CreateSub(stream.RtspPath + "_sub");
            else
                quality = stream.MainStream ?? StreamQualityConfig.CreateMain(stream.RtspPath);

            int width = quality.Width;
            int height = quality.Height;

            /*
             * MainStream은 Width/Height가 0일 수 있다.
             * Options 응답에서는 NVR이 해상도를 요구하므로 기본값을 보정한다.
             */
            if (width <= 0)
                width = isSub ? 640 : 1920;

            if (height <= 0)
                height = isSub ? 360 : 1080;

            int fps = quality.Fps > 0 ? quality.Fps : stream.Fps;

            if (fps <= 0)
                fps = isSub ? 5 : 10;

            int bitrateKbps = ParseBitrateKbps(quality.Bitrate);

            if (bitrateKbps <= 0)
                bitrateKbps = ParseBitrateKbps(stream.Bitrate);

            if (bitrateKbps <= 0)
                bitrateKbps = isSub ? 500 : 2000;

            /*
             * NVR이 선택 가능한 범위를 보는 값이다.
             * 실제 PC CAM이 ONVIF SetVideoEncoderConfiguration을 지원하지 않는 단계라면
             * 너무 넓은 범위를 주기보다 현재 설정값 중심의 안전한 범위를 준다.
             */
            int minFps = 1;
            int maxFps = fps < 1 ? 10 : Math.Max(fps, 10);

            int minBitrate = isSub ? 100 : 300;
            int maxBitrate = Math.Max(bitrateKbps, isSub ? 1000 : 3000);

            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());

            sb.Append("<trt:GetVideoEncoderConfigurationOptionsResponse>");
            sb.Append("<trt:Options>");

            sb.Append("<tt:QualityRange>");
            sb.Append("<tt:Min>1</tt:Min>");
            sb.Append("<tt:Max>10</tt:Max>");
            sb.Append("</tt:QualityRange>");

            sb.Append("<tt:H264>");

            sb.Append("<tt:ResolutionsAvailable>");
            sb.Append("<tt:Width>" + width + "</tt:Width>");
            sb.Append("<tt:Height>" + height + "</tt:Height>");
            sb.Append("</tt:ResolutionsAvailable>");

            sb.Append("<tt:GovLengthRange>");
            sb.Append("<tt:Min>1</tt:Min>");
            sb.Append("<tt:Max>" + maxFps + "</tt:Max>");
            sb.Append("</tt:GovLengthRange>");

            sb.Append("<tt:FrameRateRange>");
            sb.Append("<tt:Min>" + minFps + "</tt:Min>");
            sb.Append("<tt:Max>" + maxFps + "</tt:Max>");
            sb.Append("</tt:FrameRateRange>");

            sb.Append("<tt:EncodingIntervalRange>");
            sb.Append("<tt:Min>1</tt:Min>");
            sb.Append("<tt:Max>1</tt:Max>");
            sb.Append("</tt:EncodingIntervalRange>");

            sb.Append("<tt:H264ProfilesSupported>Baseline</tt:H264ProfilesSupported>");
            sb.Append("<tt:H264ProfilesSupported>Main</tt:H264ProfilesSupported>");

            sb.Append("</tt:H264>");

            /*
             * 일부 NVR은 Extension/H264 하위의 BitrateRange를 참조한다.
             * 표준 구현체마다 차이가 있어 최소 호환성을 위해 Extension에도 같은 범위를 제공한다.
             */
            sb.Append("<tt:Extension>");
            sb.Append("<tt:H264>");

            sb.Append("<tt:ResolutionsAvailable>");
            sb.Append("<tt:Width>" + width + "</tt:Width>");
            sb.Append("<tt:Height>" + height + "</tt:Height>");
            sb.Append("</tt:ResolutionsAvailable>");

            sb.Append("<tt:GovLengthRange>");
            sb.Append("<tt:Min>1</tt:Min>");
            sb.Append("<tt:Max>" + maxFps + "</tt:Max>");
            sb.Append("</tt:GovLengthRange>");

            sb.Append("<tt:FrameRateRange>");
            sb.Append("<tt:Min>" + minFps + "</tt:Min>");
            sb.Append("<tt:Max>" + maxFps + "</tt:Max>");
            sb.Append("</tt:FrameRateRange>");

            sb.Append("<tt:EncodingIntervalRange>");
            sb.Append("<tt:Min>1</tt:Min>");
            sb.Append("<tt:Max>1</tt:Max>");
            sb.Append("</tt:EncodingIntervalRange>");

            sb.Append("<tt:BitrateRange>");
            sb.Append("<tt:Min>" + minBitrate + "</tt:Min>");
            sb.Append("<tt:Max>" + maxBitrate + "</tt:Max>");
            sb.Append("</tt:BitrateRange>");

            sb.Append("<tt:H264ProfilesSupported>Baseline</tt:H264ProfilesSupported>");
            sb.Append("<tt:H264ProfilesSupported>Main</tt:H264ProfilesSupported>");

            sb.Append("</tt:H264>");
            sb.Append("</tt:Extension>");

            sb.Append("</trt:Options>");
            sb.Append("</trt:GetVideoEncoderConfigurationOptionsResponse>");

            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// ONVIF GetNetworkInterfaces 요청에 대한 응답 XML을 생성한다.
        /// 
        /// NVR은 장비 등록 과정에서 장비의 네트워크 인터페이스와 IP 정보를 조회할 수 있다.
        /// PC CAM은 실제 IP 카메라가 아니므로, ONVIF 서비스 접근에 사용된 host 값을 기준으로
        /// 최소 IPv4 인터페이스 정보를 반환한다.
        /// </summary>
        /// <param name="host">NVR이 접근 가능한 PC CAM 호스트 주소.</param>
        /// <returns>GetNetworkInterfaces SOAP 응답 XML.</returns>
        public string BuildGetNetworkInterfacesResponse(string host)
        {
            string ipAddress = NormalizeHost(host);

            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());

            sb.Append("<tds:GetNetworkInterfacesResponse>");
            sb.Append("<tds:NetworkInterfaces token=\"eth0\">");

            sb.Append("<tt:Enabled>true</tt:Enabled>");

            sb.Append("<tt:Info>");
            sb.Append("<tt:Name>eth0</tt:Name>");
            sb.Append("<tt:HwAddress>00:00:00:00:00:00</tt:HwAddress>");
            sb.Append("<tt:MTU>1500</tt:MTU>");
            sb.Append("</tt:Info>");

            sb.Append("<tt:IPv4>");
            sb.Append("<tt:Enabled>true</tt:Enabled>");
            sb.Append("<tt:Config>");

            sb.Append("<tt:Manual>");
            sb.Append("<tt:Address>" + XmlEscape(ipAddress) + "</tt:Address>");
            sb.Append("<tt:PrefixLength>24</tt:PrefixLength>");
            sb.Append("</tt:Manual>");

            sb.Append("<tt:DHCP>false</tt:DHCP>");

            sb.Append("</tt:Config>");
            sb.Append("</tt:IPv4>");

            sb.Append("<tt:IPv6>");
            sb.Append("<tt:Enabled>false</tt:Enabled>");
            sb.Append("</tt:IPv6>");

            sb.Append("</tds:NetworkInterfaces>");
            sb.Append("</tds:GetNetworkInterfacesResponse>");

            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

    }
}