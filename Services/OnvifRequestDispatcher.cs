using System;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// ONVIF SOAP 요청을 분석하여 적절한 응답 생성 메서드로 분기하는 서비스.
    /// 
    /// 실제 HTTP 서버는 요청 본문을 문자열로 읽은 뒤 이 클래스에 전달한다.
    /// 이 클래스는 요청 XML에 포함된 ONVIF 동작명을 기준으로 응답 XML을 반환한다.
    /// </summary>
    public class OnvifRequestDispatcher
    {
        private readonly OnvifSoapResponseBuilder _responseBuilder;

        /// <summary>
        /// ONVIF 요청 분기 처리기를 생성한다.
        /// </summary>
        /// <param name="responseBuilder">ONVIF SOAP 응답 생성기.</param>
        public OnvifRequestDispatcher(OnvifSoapResponseBuilder responseBuilder)
        {
            if (responseBuilder == null)
                throw new ArgumentNullException("responseBuilder");

            _responseBuilder = responseBuilder;
        }

        /// <summary>
        /// ONVIF SOAP 요청 문자열을 분석하고 응답 XML을 반환한다.
        /// </summary>
        /// <param name="requestBody">HTTP 요청 본문 SOAP XML.</param>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="host">NVR이 접근 가능한 PC CAM 호스트.</param>
        /// <param name="onvifPort">ONVIF HTTP 포트.</param>
        /// <returns>SOAP 응답 XML.</returns>
        public string Dispatch(
            string requestBody,
            AppConfig config,
            string host,
            int onvifPort)
        {
            string actionName = GetActionName(requestBody);

            if (string.Equals(actionName, "Empty", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildSoapFault("요청 본문이 비어 있습니다.");

            if (string.Equals(actionName, "GetDeviceInformation", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetDeviceInformationResponse();

            if (string.Equals(actionName, "GetSystemDateAndTime", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetSystemDateAndTimeResponse();

            if (string.Equals(actionName, "GetServices", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetServicesResponse(host, onvifPort);

            if (string.Equals(actionName, "GetScopes", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetScopesResponse();

            if (string.Equals(actionName, "GetCapabilities", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetCapabilitiesResponse(host, onvifPort);

            if (string.Equals(actionName, "GetProfiles", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetProfilesResponse(config);

            if (string.Equals(actionName, "GetStreamUri", StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * NVR은 GetProfiles에서 받은 ProfileToken을 다시 GetStreamUri 요청에 넣어 보낸다.
                 * 예:
                 * profile_0_main
                 * profile_0_sub
                 * profile_1_main
                 * profile_1_sub
                 * 
                 * 이 값을 기준으로 Main/Sub RTSP URL을 다르게 반환해야 한다.
                 */
                string profileToken = ExtractProfileToken(requestBody);

                return _responseBuilder.BuildGetStreamUriResponse(
                    config,
                    host,
                    profileToken);
            }

            return _responseBuilder.BuildSoapFault("지원하지 않는 ONVIF 요청입니다.");
        }

        /// <summary>
        /// 요청 본문에 특정 ONVIF 동작명이 포함되어 있는지 확인한다.
        /// 
        /// 네임스페이스 prefix가 tds, trt, 또는 임의 값일 수 있으므로
        /// 단순히 동작명 문자열 포함 여부로 판단한다.
        /// </summary>
        private bool ContainsAction(string requestBody, string actionName)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return false;

            return requestBody.IndexOf(actionName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// ONVIF SOAP 요청 본문에서 요청 액션명을 추출한다.
        /// 
        /// 현재 구현은 XML 파서를 사용하지 않고,
        /// 지원 대상 ONVIF 액션명이 요청 본문에 포함되어 있는지 확인한다.
        /// </summary>
        public string GetActionName(string requestBody)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return "Empty";

            if (ContainsAction(requestBody, "GetDeviceInformation"))
                return "GetDeviceInformation";

            if (ContainsAction(requestBody, "GetSystemDateAndTime"))
                return "GetSystemDateAndTime";

            if (ContainsAction(requestBody, "GetServices"))
                return "GetServices";

            if (ContainsAction(requestBody, "GetScopes"))
                return "GetScopes";

            if (ContainsAction(requestBody, "GetCapabilities"))
                return "GetCapabilities";

            if (ContainsAction(requestBody, "GetProfiles"))
                return "GetProfiles";

            if (ContainsAction(requestBody, "GetStreamUri"))
                return "GetStreamUri";

            return "Unknown";
        }

        /// <summary>
        /// GetStreamUri 요청 본문에서 ProfileToken 값을 추출한다.
        /// 
        /// XML 파서를 사용하지 않고 문자열 기준으로 처리한다.
        /// 일반적인 요청 형태:
        /// <trt:ProfileToken>profile_0_main</trt:ProfileToken>
        /// <ProfileToken>profile_0_sub</ProfileToken>
        /// </summary>
        /// <param name="requestBody">
        /// HTTP 요청 본문 SOAP XML.
        /// </param>
        /// <returns>
        /// ProfileToken 값.
        /// 찾지 못하면 profile_0_main을 반환한다.
        /// </returns>
        private string ExtractProfileToken(string requestBody)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return "profile_0_main";

            string token = ExtractXmlElementValue(requestBody, "ProfileToken");

            if (string.IsNullOrWhiteSpace(token))
                return "profile_0_main";

            return token.Trim();
        }

        /// <summary>
        /// XML 문자열에서 특정 태그의 값을 단순 문자열 방식으로 추출한다.
        /// 
        /// namespace prefix가 있을 수 있으므로 아래 두 형태를 모두 고려한다.
        /// - <ProfileToken>...</ProfileToken>
        /// - <trt:ProfileToken>...</trt:ProfileToken>
        /// </summary>
        /// <param name="xml">
        /// XML 문자열.
        /// </param>
        /// <param name="elementName">
        /// 찾을 태그명.
        /// </param>
        /// <returns>
        /// 태그 내부 값.
        /// 찾지 못하면 빈 문자열.
        /// </returns>
        private string ExtractXmlElementValue(string xml, string elementName)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return "";

            if (string.IsNullOrWhiteSpace(elementName))
                return "";

            /*
             * 1. prefix 없는 형태 우선 검색.
             */
            string directStartTag = "<" + elementName + ">";
            string directEndTag = "</" + elementName + ">";

            string value = ExtractBetween(xml, directStartTag, directEndTag);

            if (!string.IsNullOrWhiteSpace(value))
                return value;

            /*
             * 2. namespace prefix가 있는 형태 검색.
             * 예: <trt:ProfileToken>...</trt:ProfileToken>
             */
            string startPattern = ":" + elementName + ">";

            int startIndex = xml.IndexOf(startPattern, StringComparison.OrdinalIgnoreCase);

            if (startIndex < 0)
                return "";

            startIndex = startIndex + startPattern.Length;

            int endIndex = xml.IndexOf("</", startIndex, StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0 || endIndex <= startIndex)
                return "";

            return xml.Substring(startIndex, endIndex - startIndex);
        }

        /// <summary>
        /// 문자열에서 시작 태그와 종료 태그 사이의 값을 추출한다.
        /// </summary>
        private string ExtractBetween(string source, string startText, string endText)
        {
            int startIndex = source.IndexOf(startText, StringComparison.OrdinalIgnoreCase);

            if (startIndex < 0)
                return "";

            startIndex += startText.Length;

            int endIndex = source.IndexOf(endText, startIndex, StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0 || endIndex <= startIndex)
                return "";

            return source.Substring(startIndex, endIndex - startIndex);
        }
    }
}