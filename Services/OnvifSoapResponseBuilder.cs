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
        /// ONVIF Media Profile 목록 응답을 생성한다.
        /// 
        /// 초기 단계에서는 Stream0 하나를 하나의 Profile로 반환한다.
        /// NVR은 이 Profile Token을 사용해 GetStreamUri를 요청한다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <returns>GetProfiles SOAP 응답 XML.</returns>
        public string BuildGetProfilesResponse(AppConfig config)
        {
            StreamConfig stream = FindStream(config, 0);

            string profileToken = "profile_0";
            string profileName = stream != null && !string.IsNullOrWhiteSpace(stream.ScreenName)
                ? stream.ScreenName
                : "PC_CAM_STREAM_0";

            int fps = stream != null ? stream.Fps : 5;
            string codec = stream != null ? stream.Codec : "H264";

            StringBuilder sb = new StringBuilder();

            sb.Append(CreateEnvelopeStart());
            sb.Append("<trt:GetProfilesResponse>");

            sb.Append("<trt:Profiles token=\"" + XmlEscape(profileToken) + "\" fixed=\"true\">");
            sb.Append("<tt:Name>" + XmlEscape(profileName) + "</tt:Name>");

            sb.Append("<tt:VideoSourceConfiguration token=\"video_source_0\">");
            sb.Append("<tt:Name>VideoSource0</tt:Name>");
            sb.Append("<tt:UseCount>1</tt:UseCount>");
            sb.Append("<tt:SourceToken>source_0</tt:SourceToken>");
            sb.Append("<tt:Bounds x=\"0\" y=\"0\" width=\"1920\" height=\"1080\" />");
            sb.Append("</tt:VideoSourceConfiguration>");

            sb.Append("<tt:VideoEncoderConfiguration token=\"video_encoder_0\">");
            sb.Append("<tt:Name>VideoEncoder0</tt:Name>");
            sb.Append("<tt:UseCount>1</tt:UseCount>");
            sb.Append("<tt:Encoding>" + NormalizeOnvifCodec(codec) + "</tt:Encoding>");
            sb.Append("<tt:Resolution>");
            sb.Append("<tt:Width>1920</tt:Width>");
            sb.Append("<tt:Height>1080</tt:Height>");
            sb.Append("</tt:Resolution>");
            sb.Append("<tt:Quality>5</tt:Quality>");
            sb.Append("<tt:RateControl>");
            sb.Append("<tt:FrameRateLimit>" + fps + "</tt:FrameRateLimit>");
            sb.Append("<tt:EncodingInterval>1</tt:EncodingInterval>");
            sb.Append("<tt:BitrateLimit>1200</tt:BitrateLimit>");
            sb.Append("</tt:RateControl>");
            sb.Append("</tt:VideoEncoderConfiguration>");

            sb.Append("</trt:Profiles>");
            sb.Append("</trt:GetProfilesResponse>");
            sb.Append(CreateEnvelopeEnd());

            return sb.ToString();
        }

        /// <summary>
        /// ONVIF Stream URI 응답을 생성한다.
        /// 
        /// NVR은 GetStreamUri 응답으로 받은 RTSP 주소를 사용해 실제 영상을 가져간다.
        /// 현재 PC CAM 구조에서는 MediaMTX가 RTSP를 제공하므로,
        /// 응답 URI는 MediaMTX의 RTSP 주소를 반환한다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="rtspHost">NVR이 접근 가능한 PC CAM 호스트 주소.</param>
        /// <param name="streamNo">스트림 번호.</param>
        /// <returns>GetStreamUri SOAP 응답 XML.</returns>
        public string BuildGetStreamUriResponse(
            AppConfig config,
            string rtspHost,
            int streamNo)
        {
            string rtspUrl = BuildRtspUrl(config, rtspHost, streamNo);

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
        /// RTSP Read 주소를 생성한다.
        /// 
        /// 현재 MediaMTX에는 RTSP readUser/readPass가 적용될 수 있으므로,
        /// 초기 구현에서는 ONVIF 계정 정보를 RTSP URL에 포함한다.
        /// 향후 NVR 호환성에 따라 계정 포함 여부를 옵션화할 수 있다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="host">NVR이 접근 가능한 PC CAM 호스트.</param>
        /// <param name="streamNo">스트림 번호.</param>
        /// <returns>RTSP URL.</returns>
        private string BuildRtspUrl(AppConfig config, string host, int streamNo)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (config.RtspServer == null)
                throw new InvalidOperationException("RTSP 서버 설정이 없습니다.");

            StreamConfig stream = FindStream(config, streamNo);

            if (stream == null)
                throw new InvalidOperationException("스트림 설정을 찾을 수 없습니다. StreamNo=" + streamNo);

            string rtspHost = NormalizeHost(host);
            string path = NormalizeRtspPath(stream.RtspPath);

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
    }
}