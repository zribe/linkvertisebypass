using System.Security.Cryptography;

namespace LinkvertiseBypass;

internal static class CheqFingerprint
{
    private const string Endpoint = "https://obseu.bizseasky.com/ct";
    // This is the stable browser telemetry template expected by the CHEQ collector.
    private const string Data =
        "W1siZWYiLDY5OTddLFsiYWJuY2giLDEwXSxbLTExLCJ7XCJ0XCI6XCJcIixcIm1cIjpbXCJkZXNjcmlwdGlvblwiXX0iXSxbLTI5LCItIl0sWy0zMSwiZmFs" +
        "c2UiXSxbLTQxLCItIl0sWy03MywiRW10MUpUOWtPeVltUFdrbkxtUS9MRHM2SUNZbmRCVnJlM2huZTJkNGVoVnJhUlluTGlFbU9qMWtKeTVrS25oK2ZubDdl" +
        "MzU2Y0h4MEZXc1ZhM2QxYUdSa1pHUjNkUzBnUDJrV0p5NHFKaWM5TENjOVpDY3VaQ3A0Zm41NWUzdCtlbkI4ZEJWckZXdHBLaVVvT2pwMEZXc29KU3c3UFdr" +
        "bkxtUTZQU2c3WkNBbk9pdzdQU3d0Rld0M2RXaGtaR1JrZDNWbUxTQS9kM1V0SUQ5cEZpY3VLbXRsYTNVcUltUStJQzB1TEQxcExTZzlLR1E1Smo0c095d3Ra" +
        "Q3N3ZEJWckNpRThPeWNpTERCbktpWnBDaWduS2l3bGFROGxKajQ2Rld0cExTZzlLR1EvWkNnNU9YUVZheFZyZDNVdElEOXBLaVVvT2pwMEZXc3FJbVE2UFRB" +
        "bExCVnJkM1ZvWkdSa1pIZDFhR1JrWkdSM2RXWXRJRDkzZFdZcUltUStJQzB1TEQxM2EyVnJkU29pWkRrb1BEb3NaRDRvSlNWcExTZzlLR1E1Smo0c095d3Ra" +
        "Q3N3ZEJWckNpRThPeWNpTERCbktpWnBDaWduS2l3bGFROGxKajQ2Rld0cExTZzlLR1EvWkNnNU9YUVZheFZyZDNWb1pHUmtaSGQxWmlvaVpEa29QRG9zWkQ0" +
        "b0pTVjNheFE9Il0sWzEyLCJ7XCJjdHhcIjpcIndlYmdsXCIsXCJ2XCI6XCJnb29nbGUgaW5jLiAobWljcm9zb2Z0KVwiLFwiclwiOlwiYW5nbGUgKG1pY3Jv" +
        "c29mdCwgbWljcm9zb2Z0IGJhc2ljIHJlbmRlciBkcml2ZXIgKDB4MDAwMDAwOGMpIGRpcmVjdDNkMTEgdnNfNV8wIHBzXzVfMCwgZDNkMTEpXCIsXCJzbHZc" +
        "IjpcIndlYmdsIGdsc2wgZXMgMS4wIChvcGVuZ2wgZXMgZ2xzbCBlcyAxLjAgY2hyb21pdW0pXCIsXCJndmVyXCI6XCJ3ZWJnbCAxLjAgKG9wZW5nbCBlcyAy" +
        "LjAgY2hyb21pdW0pXCIsXCJndmVuXCI6XCJ3ZWJraXRcIixcImJlblwiOjYsXCJ3Z2xcIjoxLFwiZ3JlblwiOlwid2Via2l0IHdlYmdsXCIsXCJzZWZcIjo3" +
        "MzIwNzIzNjYsXCJzZWNcIjpcIlwifSJdLFstMTAsIi0iXSxbLTE5LCJbMTAsMTAsMTAsMTAsMCwwLDEsMjQsMjQsXCItXCIsODAwLDYwMCw4MDAsNjAwLDc4" +
        "MCw1ODAsNzU2LDQ0MSwwLDAsMCwwLFwiLVwiLFwiLVwiLDc1Niw0NDEsMF0iXSxbLTIyLCJbXCJuXCIsXCJuXCJdIl0sWy00MywiMDAxMDAwMDEwMTAwMDAw" +
        "MTEwMTExMDExMDExMDExMDEwMDAwMDEwMTEwMTAiXSxbLTU5LCJkZWZhdWx0Il0sWy02NywiLSJdLFstOCwiLSJdLFstMTUsIi0iXSxbLTE4LCJbMCwwLDAs" +
        "MV0iXSxbLTUwLCItIl0sWy01MSwiLSJdLFstNzYsIi0iXSxbLTE2LCIwIl0sWy0yMywiKyJdLFstMjQsIltdIl0sWy00NSwiNjIwLDAsMCwwLDAsMCwwLDAs" +
        "MCwwLDAsNjUzLDAsMCwwLDY3MiwwLDY3MiwwLDY4MywwLDAsMCwwLDAsMCwwLDY3NiwwLDY3NiwwLDYxNyJdLFstNjIsIjgwIl0sWy02OCwiLSJdLFstMywi" +
        "W1wiaW50ZXJuYWwtcGRmLXZpZXdlclwiLFwiaW50ZXJuYWwtcGRmLXZpZXdlclwiLFwiaW50ZXJuYWwtcGRmLXZpZXdlclwiLFwiaW50ZXJuYWwtcGRmLXZp" +
        "ZXdlclwiLFwiaW50ZXJuYWwtcGRmLXZpZXdlclwiXSJdLFstMTMsIi0iXSxbLTQwLCIzMyJdLFstNTYsImxhbmRzY2FwZS1wcmltYXJ5Il0sWy00LCItIl0s" +
        "Wy0yNywiWzAsMS43NSwwLFwiNGdcIixudWxsLFwiLVwiXSJdLFstMzMsIi0iXSxbLTQyLCI4ODMzOTkwMTYiXSxbLTQ3LCJBc2lhL0plcnVzYWxlbSxlbi1V" +
        "UyxsYXRuLGdyZWdvcnkiXSxbLTQ4LCJbXCItXCIsXCItXCIsXCItXCIsXCItXCIsXCItXCJdIl0sWy02NSwiLSJdLFstMSwiLSJdLFstMTIsIm51bGwiXSxb" +
        "LTM0LCItIl0sWy00NiwiMCJdLFstNTUsIjIiXSxbLTYwLDIwOF0sWy02OSwiV2luZG93c3xHb29nbGUgSW5jLnwxNnw4fHwwIl0sWy03LCItIl0sWy05LCIr" +
        "Il0sWy0xNCwiLSJdLFstMTcsIjgiXSxbLTI1LCItIl0sWy0zMCwiW1widlwiLDBdIl0sWy00NCwiMCwwLDAsNSJdLFstNTMsIjAwMSJdLFstNjEsIntcIndn" +
        "c2xcIjpcIjEwO3BhY2tlZF80eDhfaW50ZWdlcl9kb3RfcHJvZHVjdDtzdWJncm91cF91bmlmb3JtaXR5O2ltbWVkaWF0ZV9hZGRyZXNzX3NwYWNlO2xpbmVh" +
        "cl9pbmRleGluZztzdWJncm91cF9pZDtyZWFkb25seV9hbmRfcmVhZHdyaXRlX3N0b3JhZ2VfdGV4dHVyZXM7dW5yZXN0cmljdGVkX3BvaW50ZXJfcGFyYW1l" +
        "dGVyczt0ZXh0dXJlX2FuZF9zYW1wbGVyX2xldDtwb2ludGVyX2NvbXBvc2l0ZV9hY2Nlc3M7dW5pZm9ybV9idWZmZXJfc3RhbmRhcmRfbGF5b3V0O1wiLFwi" +
        "cGNmXCI6XCJiZ3JhOHVub3JtXCJ9Il0sWy03MSwiYTAxMDAxMDExMDAxMDAxMDEwMDAxMDEwMDExMTEwMTEwMDAxMDEiXSxbLTIxLCItIl0sWy0yNiwie1wi" +
        "dGpoc1wiOjM1MjU3NDA2LFwidWpoc1wiOjI5OTc4MDc0LFwiamhzbFwiOjQzOTU2MzA1OTJ9Il0sWy0yOCwiZW4tVVMsZW4iXSxbLTM1LCJbMTc4ODcyNzQw" +
        "ODU2MSwtM10iXSxbLTM2LCJbXCI0LzNcIixcIjQvM1wiXSJdLFstMzcsIi0xMDktNjYtNzAtIl0sWy0zOCwiaSwtMSwtMSwyLDAsMSwwLDg1LDE1LDQwLC0x" +
        "LDAsMzIwLDUwNCw2ODUsNjg1Il0sWy0zOSwiW1wiMjAwMzAxMDdcIiwyLFwiR2Vja29cIixcIk5ldHNjYXBlXCIsXCJNb3ppbGxhXCIsbnVsbCxudWxsLHRy" +
        "dWUsMTYsZmFsc2UsbnVsbCw1LHRydWUsdHJ1ZSxudWxsLDAsdHJ1ZSx0cnVlLHRydWUsdHJ1ZV0iXSxbLTQ5LCItIl0sWy02MywiLSJdLFstNjQsIlswLFwi" +
        "XCIsW11dIl0sWy0yLCI0LFdUWkxJQUlhd2hoSDFWVUZieEtTQXFJb0x5Uk53VkZCVlFFVkZ4UThGOWYwOVFYSEJGSDhMREJkd1FRVUQyZllld2hTU1RaREl6" +
        "ZCtudTJ2N2YwL2ZlWVJJVzBmLzcvWCsvLzYsMjY0ODExODU3NyJdLFstNSwiLSJdLFstNiwie1wid1wiOltcIjBcIixcInYzNzdcIixcImNiSnNvblBcIixc" +
        "Il9fY29yZS1qc19zaGFyZWRfX1wiLFwiY2Jfd2luZG93X2xvZ2dlclwiLFwiX19TRU5UUllfX1wiLFwic2V0SW1tZWRpYXRlXCIsXCJjbGVhckltbWVkaWF0" +
        "ZVwiLFwiQ2hhcmdlYmVlXCIsXCJpc0FkQmxvY2tBY3RpdmVcIixcIndlYnBhY2tDaHVua2NyZWF0b3Jfd2ViXCIsXCJab25lXCIsXCJfX3pvbmVfc3ltYm9s" +
        "X19Qcm9taXNlXCIsXCJfX3pvbmVfc3ltYm9sX19mZXRjaFwiLFwiX196b25lX3N5bWJvbF9fc2V0VGltZW91dFwiLFwiX196b25lX3N5bWJvbF9fY2xlYXJU" +
        "aW1lb3V0XCIsXCJfX3pvbmVfc3ltYm9sX19zZXRJbnRlcnZhbFwiLFwiX196b25lX3N5bWJvbF9fY2xlYXJJbnRlcnZhbFwiLFwiX196b25lX3N5bWJvbF9f" +
        "c2V0SW1tZWRpYXRlXCIsXCJfX3pvbmVfc3ltYm9sX19jbGVhckltbWVkaWF0ZVwiLFwiX196b25lX3N5bWJvbF9fcmVxdWVzdEFuaW1hdGlvbkZyYW1lXCIs" +
        "XCJfX3pvbmVfc3ltYm9sX19jYW5jZWxBbmltYXRpb25GcmFtZVwiLFwiX196b25lX3N5bWJvbF9fd2Via2l0UmVxdWVzdEFuaW1hdGlvbkZyYW1lXCIsXCJf" +
        "X3pvbmVfc3ltYm9sX193ZWJraXRDYW5jZWxBbmltYXRpb25GcmFtZVwiLFwiX196b25lX3N5bWJvbF9fYWxlcnRcIixcIl9fem9uZV9zeW1ib2xfX3Byb21w" +
        "dFwiLFwiX196b25lX3N5bWJvbF9fY29uZmlybVwiLFwiX196b25lX3N5bWJvbF9fTXV0YXRpb25PYnNlcnZlclwiLFwiX196b25lX3N5bWJvbF9fV2ViS2l0" +
        "TXV0YXRpb25PYnNlcnZlclwiLFwiX196b25lX3N5bWJvbF9fSW50ZXJzZWN0aW9uT2JzZXJ2ZXJcIixcIl9fem9uZV9zeW1ib2xfX0ZpbGVSZWFkZXJcIixc" +
        "Il9fem9uZV9zeW1ib2xfX29ub25wYWdlcmV2ZWFscGF0Y2hlZFwiLFwiX196b25lX3N5bWJvbF9fb25vbnNlYXJjaHBhdGNoZWRcIixcIl9fem9uZV9zeW1i" +
        "b2xfX29ub25hcHBpbnN0YWxsZWRwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uYmVmb3JlaW5zdGFsbHByb21wdHBhdGNoZWRcIixcIl9fem9uZV9z" +
        "eW1ib2xfX29ub25hYm9ydHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25iZWZvcmVpbnB1dHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25i" +
        "ZWZvcmVtYXRjaHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25iZWZvcmV0b2dnbGVwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uYmx1cnBh" +
        "dGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jYW5jZWxwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uY2FucGxheXBhdGNoZWRcIixcIl9fem9u" +
        "ZV9zeW1ib2xfX29ub25jYW5wbGF5dGhyb3VnaHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jaGFuZ2VwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9s" +
        "X19vbm9uY2xpY2twYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uY2xvc2VwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uY29tbWFuZHBhdGNo" +
        "ZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jb250ZW50dmlzaWJpbGl0eWF1dG9zdGF0ZWNoYW5nZXBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25j" +
        "b250ZXh0bG9zdHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jb250ZXh0bWVudXBhdGNoZWRcIl0sXCJuXCI6W10sXCJkXCI6W119Il0sWy01NCwi" +
        "e1wiaFwiOltcIl8zXCIsXCIyODcyODk5MzIwXCIsXCI0MTA0MzE2MjNcIixcIjI2MzkyMjI0NjhcIixcIl8xXCIsXCI4OTY0Nzc3MTZcIl0sXCJkXCI6W10s" +
        "XCJiXCI6W1wiXzFcIixcIjEwNjQ4MjI2ODdcIixcIjQxMDQzMTYyM1wiLFwiMjYzOTIyMjQ2OFwiXSxcInNcIjoxfSJdLFstNTcsIldFMFplRXRMV0VBWGZG" +
        "d1pFVkZOVFVsS0F4WVdYRXhXV3hkYlVFTktYRmhLVWtBWFdsWlVGa3BCU1JaUUZsMWZBUXRhRFZ4ZkR3d0tEMXdOWFZ4Y0R3a1BDUWdMQVFsYldnRUpEQUVC" +
        "RjFOS0JsQmRCQWdORFE0S0F3Z0RBQUFLQ1FvUUZWaE5HVXNaRVZGTlRVbEtBeFlXWEV4V1d4ZGJVRU5LWEZoS1VrQVhXbFpVRmtwQlNSWlFGbDFmQVF0YURW" +
        "eGZEd3dLRDF3TlhWeGNEd2tQQ1FnTEFRbGJXZ0VKREFFQkYxTktCbEJkQkFnTkRRNEtBd2dEQ0FrTUNBb1BFQlZZVFJsUlRVMUpTZ01XRmx4TVZsc1hXMUJE" +
        "U2x4WVNsSkFGMXBXVkJaS1FVa1dVQlpkWHdFTFdnMWNYdzhNQ2c5Y0RWMWNYQThKRHdrSUN3RUpXdz09Il0sWy01OCwiLSJdLFstNzAsIi0iXSxbLTcyLCJF" +
        "eFU9Il0sWy03NCwiMCwwIl0sWy03NSwie30uc29tZUZ1bmMgaXMgbm90IGEgZnVuY3Rpb24iXSxbImJuY2giLDk0XSxbLTIwLCItIl0sWy0zMiwiMCJdLFst" +
        "NTIsIjEyLDM0MjQ2NjUzNjgiXSxbLTY2LCJnZW9sb2NhdGlvbixjaHVhZnVsbHZlcnNpb25saXN0LGNyb3Nzb3JpZ2luaXNvbGF0ZWQsc2NyZWVud2FrZWxv" +
        "Y2ssb25kZXZpY2VzcGVlY2hyZWNvZ25pdGlvbix0cmFuc2xhdG9yLHB1YmxpY2tleWNyZWRlbnRpYWxzZ2V0LHNoYXJlZHN0b3JhZ2VzZWxlY3R1cmwsY2h1" +
        "YWFyY2gsYmx1ZXRvb3RoLGNvbXB1dGVwcmVzc3VyZSxjaHByZWZlcnNyZWR1Y2VkdHJhbnNwYXJlbmN5LGRlZmVycmVkZmV0Y2gsdXNiLGNoc2F2ZWRhdGEs" +
        "cHVibGlja2V5Y3JlZGVudGlhbHNjcmVhdGUsc2hhcmVkc3RvcmFnZSxkZWZlcnJlZGZldGNobWluaW1hbCxydW5hZGF1Y3Rpb24sY2hkb3dubGluayxjaHVh" +
        "Zm9ybWZhY3RvcnMsb3RwY3JlZGVudGlhbHMscGF5bWVudCxjaHVhLGNodWFtb2RlbCxjaGVjdCxhdXRvcGxheSxjYW1lcmEsbGFuZ3VhZ2VkZXRlY3Rvcixw" +
        "cml2YXRlc3RhdGV0b2tlbmlzc3VhbmNlLGRpZ2l0YWxjcmVkZW50aWFsc2dldCxhY2NlbGVyb21ldGVyLGNodWFwbGF0Zm9ybXZlcnNpb24saWRsZWRldGVj" +
        "dGlvbixwcml2YXRlYWdncmVnYXRpb24saW50ZXJlc3Rjb2hvcnQsY2h2aWV3cG9ydGhlaWdodCxjYXB0dXJlZHN1cmZhY2Vjb250cm9sLGxvY2FsZm9udHMs" +
        "Y2h1YXBsYXRmb3JtLG1pZGksY2h1YWZ1bGx2ZXJzaW9uLHhyc3BhdGlhbHRyYWNraW5nLGNsaXBib2FyZHJlYWQsZ2FtZXBhZCxkaXNwbGF5Y2FwdHVyZSxr" +
        "ZXlib2FyZG1hcCxqb2luYWRpbnRlcmVzdGdyb3VwLGFyaWFub3RpZnksbG9jYWxuZXR3b3JrLGNodWFoaWdoZW50cm9weXZhbHVlcyxjaHdpZHRoLGNocHJl" +
        "ZmVyc3JlZHVjZWRtb3Rpb24sYnJvd3Npbmd0b3BpY3MsZW5jcnlwdGVkbWVkaWEsbG9jYWxuZXR3b3JrYWNjZXNzLGd5cm9zY29wZSxzZXJpYWwsY2hydHQs" +
        "Y2h1YW1vYmlsZSx3aW5kb3dtYW5hZ2VtZW50LHVubG9hZCxjaGRwcixjaHByZWZlcnNjb2xvcnNjaGVtZSxjaHVhd293NjQsZnVsbHNjcmVlbixpZGVudGl0" +
        "eWNyZWRlbnRpYWxzZ2V0LHByaXZhdGVzdGF0ZXRva2VucmVkZW1wdGlvbixoaWQsc3VtbWFyaXplcixjaHVhYml0bmVzcyxzdG9yYWdlYWNjZXNzLHN5bmN4" +
        "aHIsY2hkZXZpY2VtZW1vcnksY2h2aWV3cG9ydHdpZHRoLHBpY3R1cmVpbnBpY3R1cmUsbG9vcGJhY2tuZXR3b3JrLG1hZ25ldG9tZXRlcixjbGlwYm9hcmR3" +
        "cml0ZSxtaWNyb3Bob25lIl0sWyJkZGIiLCIwLDQsMCwwLDAsMSwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAs" +
        "MCwwLDAsMSwwLDAsMCwwLDAsMCwxLDEsMzEsMCw2LDAsMSwwLDAsMTQsMCwwLDAsMCwwLDAsMCwwLDEsMCwwLDAsMCwwLDAsMCwwLDEsMCwyLDEsMCwwLDAi" +
        "XSxbImNiIiwiMCwwLDAsMCwwLDAsMCwwLDAsNywwLDAsNiwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCww" +
        "LDAsMCwwLDEsMCwwLDEsMCwwLDAsMCwxLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDMsMCwwLDEsMCwwLDAsMCwxLDAsMCww" +
        "LDAsMSwxLDAsMCwwLDAsMCwwLDAsMCJdXQ==";

    // Fresh request and session identifiers are added around the fingerprint template.
    public static string CollectorUrl(string canonical)
    {
        var now = DateTimeOffset.UtcNow;
        var values = new (string Key, string Value)[]
        {
            ("id", "14473"), ("url", canonical), ("sf", "0"), ("tpi", ""), ("ch", "cheq4ppc"),
            ("uvid", ""), ("tsf", "0"), ("tsfmi", ""), ("tsfu", ""), ("cb", now.ToUnixTimeMilliseconds().ToString()),
            ("hl", "2"), ("op", "0"), ("ag", "444981139"), ("rand", RandomDigits(97)), ("fs", "756x441"),
            ("fst", "756x441"), ("np", "windows"), ("nv", "google inc."), ("ref", ""), ("ss", "800x600"),
            ("nc", "0"), ("at", ""), ("di", Data), ("dep", "0"), ("pre", "0"),
            ("sdd", ""), ("cri", "qR3HM3UpbF"), ("pto", RandomNumberGenerator.GetInt32(400, 901).ToString()), ("ver", "66"), ("gac", "-"),
            ("mei", ""), ("ap", ""), ("fe", "1"), ("duid", CollectorId(now.ToUnixTimeSeconds())), ("suid", CollectorId(now.ToUnixTimeSeconds())),
            ("tuid", CollectorId(now.ToUnixTimeSeconds())), ("sid", CollectorId(now.ToUnixTimeMilliseconds())), ("fbc", "-"), ("gtm", "WyJvcGVuUGFnZSJd"),
            ("it", "64,420,109"), ("fbcl", "-"), ("gacl", "-"), ("gacsd", "-"), ("rtic", "-"),
            ("rtict", "-"), ("bgc", "-"), ("spa", "1"), ("urid", "0"), ("ab", ""),
            ("sck", "-"), ("io", "aGA2Oi1+aG02Og==")
        };
        return Endpoint + "?" + string.Join("&", values.Select(value => Uri.EscapeDataString(value.Key) + "=" + Uri.EscapeDataString(value.Value)));
    }

    private static string CollectorId(long timestamp) => $"1.{timestamp}.{RandomToken(16)}";

    private static string RandomDigits(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        var value = new char[length];
        for (var index = 0; index < length; index++) value[index] = (char)('0' + bytes[index] % 10);
        if (value[0] == '0') value[0] = '1';
        return new string(value);
    }

    private static string RandomToken(int length)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var value = new char[length];
        for (var index = 0; index < length; index++) value[index] = alphabet[bytes[index] % alphabet.Length];
        return new string(value);
    }
}
