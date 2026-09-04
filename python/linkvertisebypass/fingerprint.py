from __future__ import annotations

import secrets
import string
import time
from urllib.parse import urlencode

COLLECTOR_ENDPOINT = "https://obseu.bizseasky.com/ct"
# This is the stable browser telemetry template expected by the CHEQ collector.
CHEQ_FINGERPRINT = (
    "W1siZWYiLDY5OTddLFsiYWJuY2giLDEwXSxbLTExLCJ7XCJ0XCI6XCJcIixcIm1cIjpbXCJkZXNjcmlwdGlvblwiXX0iXSxbLTI5"
    "LCItIl0sWy0zMSwiZmFsc2UiXSxbLTQxLCItIl0sWy03MywiRW10MUpUOWtPeVltUFdrbkxtUS9MRHM2SUNZbmRCVnJlM2huZTJk"
    "NGVoVnJhUlluTGlFbU9qMWtKeTVrS25oK2ZubDdlMzU2Y0h4MEZXc1ZhM2QxYUdSa1pHUjNkUzBnUDJrV0p5NHFKaWM5TENjOVpD"
    "Y3VaQ3A0Zm41NWUzdCtlbkI4ZEJWckZXdHBLaVVvT2pwMEZXc29KU3c3UFdrbkxtUTZQU2c3WkNBbk9pdzdQU3d0Rld0M2RXaGta"
    "R1JrZDNWbUxTQS9kM1V0SUQ5cEZpY3VLbXRsYTNVcUltUStJQzB1TEQxcExTZzlLR1E1Smo0c095d3RaQ3N3ZEJWckNpRThPeWNp"
    "TERCbktpWnBDaWduS2l3bGFROGxKajQ2Rld0cExTZzlLR1EvWkNnNU9YUVZheFZyZDNVdElEOXBLaVVvT2pwMEZXc3FJbVE2UFRB"
    "bExCVnJkM1ZvWkdSa1pIZDFhR1JrWkdSM2RXWXRJRDkzZFdZcUltUStJQzB1TEQxM2EyVnJkU29pWkRrb1BEb3NaRDRvSlNWcExT"
    "ZzlLR1E1Smo0c095d3RaQ3N3ZEJWckNpRThPeWNpTERCbktpWnBDaWduS2l3bGFROGxKajQ2Rld0cExTZzlLR1EvWkNnNU9YUVZh"
    "eFZyZDNWb1pHUmtaSGQxWmlvaVpEa29QRG9zWkQ0b0pTVjNheFE9Il0sWzEyLCJ7XCJjdHhcIjpcIndlYmdsXCIsXCJ2XCI6XCJn"
    "b29nbGUgaW5jLiAobWljcm9zb2Z0KVwiLFwiclwiOlwiYW5nbGUgKG1pY3Jvc29mdCwgbWljcm9zb2Z0IGJhc2ljIHJlbmRlciBk"
    "cml2ZXIgKDB4MDAwMDAwOGMpIGRpcmVjdDNkMTEgdnNfNV8wIHBzXzVfMCwgZDNkMTEpXCIsXCJzbHZcIjpcIndlYmdsIGdsc2wg"
    "ZXMgMS4wIChvcGVuZ2wgZXMgZ2xzbCBlcyAxLjAgY2hyb21pdW0pXCIsXCJndmVyXCI6XCJ3ZWJnbCAxLjAgKG9wZW5nbCBlcyAy"
    "LjAgY2hyb21pdW0pXCIsXCJndmVuXCI6XCJ3ZWJraXRcIixcImJlblwiOjYsXCJ3Z2xcIjoxLFwiZ3JlblwiOlwid2Via2l0IHdl"
    "YmdsXCIsXCJzZWZcIjo3MzIwNzIzNjYsXCJzZWNcIjpcIlwifSJdLFstMTAsIi0iXSxbLTE5LCJbMTAsMTAsMTAsMTAsMCwwLDEs"
    "MjQsMjQsXCItXCIsODAwLDYwMCw4MDAsNjAwLDc4MCw1ODAsNzU2LDQ0MSwwLDAsMCwwLFwiLVwiLFwiLVwiLDc1Niw0NDEsMF0i"
    "XSxbLTIyLCJbXCJuXCIsXCJuXCJdIl0sWy00MywiMDAxMDAwMDEwMTAwMDAwMTEwMTExMDExMDExMDExMDEwMDAwMDEwMTEwMTAi"
    "XSxbLTU5LCJkZWZhdWx0Il0sWy02NywiLSJdLFstOCwiLSJdLFstMTUsIi0iXSxbLTE4LCJbMCwwLDAsMV0iXSxbLTUwLCItIl0s"
    "Wy01MSwiLSJdLFstNzYsIi0iXSxbLTE2LCIwIl0sWy0yMywiKyJdLFstMjQsIltdIl0sWy00NSwiNjIwLDAsMCwwLDAsMCwwLDAs"
    "MCwwLDAsNjUzLDAsMCwwLDY3MiwwLDY3MiwwLDY4MywwLDAsMCwwLDAsMCwwLDY3NiwwLDY3NiwwLDYxNyJdLFstNjIsIjgwIl0s"
    "Wy02OCwiLSJdLFstMywiW1wiaW50ZXJuYWwtcGRmLXZpZXdlclwiLFwiaW50ZXJuYWwtcGRmLXZpZXdlclwiLFwiaW50ZXJuYWwt"
    "cGRmLXZpZXdlclwiLFwiaW50ZXJuYWwtcGRmLXZpZXdlclwiLFwiaW50ZXJuYWwtcGRmLXZpZXdlclwiXSJdLFstMTMsIi0iXSxb"
    "LTQwLCIzMyJdLFstNTYsImxhbmRzY2FwZS1wcmltYXJ5Il0sWy00LCItIl0sWy0yNywiWzAsMS43NSwwLFwiNGdcIixudWxsLFwi"
    "LVwiXSJdLFstMzMsIi0iXSxbLTQyLCI4ODMzOTkwMTYiXSxbLTQ3LCJBc2lhL0plcnVzYWxlbSxlbi1VUyxsYXRuLGdyZWdvcnki"
    "XSxbLTQ4LCJbXCItXCIsXCItXCIsXCItXCIsXCItXCIsXCItXCJdIl0sWy02NSwiLSJdLFstMSwiLSJdLFstMTIsIm51bGwiXSxb"
    "LTM0LCItIl0sWy00NiwiMCJdLFstNTUsIjIiXSxbLTYwLDIwOF0sWy02OSwiV2luZG93c3xHb29nbGUgSW5jLnwxNnw4fHwwIl0s"
    "Wy03LCItIl0sWy05LCIrIl0sWy0xNCwiLSJdLFstMTcsIjgiXSxbLTI1LCItIl0sWy0zMCwiW1widlwiLDBdIl0sWy00NCwiMCww"
    "LDAsNSJdLFstNTMsIjAwMSJdLFstNjEsIntcIndnc2xcIjpcIjEwO3BhY2tlZF80eDhfaW50ZWdlcl9kb3RfcHJvZHVjdDtzdWJn"
    "cm91cF91bmlmb3JtaXR5O2ltbWVkaWF0ZV9hZGRyZXNzX3NwYWNlO2xpbmVhcl9pbmRleGluZztzdWJncm91cF9pZDtyZWFkb25s"
    "eV9hbmRfcmVhZHdyaXRlX3N0b3JhZ2VfdGV4dHVyZXM7dW5yZXN0cmljdGVkX3BvaW50ZXJfcGFyYW1ldGVyczt0ZXh0dXJlX2Fu"
    "ZF9zYW1wbGVyX2xldDtwb2ludGVyX2NvbXBvc2l0ZV9hY2Nlc3M7dW5pZm9ybV9idWZmZXJfc3RhbmRhcmRfbGF5b3V0O1wiLFwi"
    "cGNmXCI6XCJiZ3JhOHVub3JtXCJ9Il0sWy03MSwiYTAxMDAxMDExMDAxMDAxMDEwMDAxMDEwMDExMTEwMTEwMDAxMDEiXSxbLTIx"
    "LCItIl0sWy0yNiwie1widGpoc1wiOjM1MjU3NDA2LFwidWpoc1wiOjI5OTc4MDc0LFwiamhzbFwiOjQzOTU2MzA1OTJ9Il0sWy0y"
    "OCwiZW4tVVMsZW4iXSxbLTM1LCJbMTc4ODcyNzQwODU2MSwtM10iXSxbLTM2LCJbXCI0LzNcIixcIjQvM1wiXSJdLFstMzcsIi0x"
    "MDktNjYtNzAtIl0sWy0zOCwiaSwtMSwtMSwyLDAsMSwwLDg1LDE1LDQwLC0xLDAsMzIwLDUwNCw2ODUsNjg1Il0sWy0zOSwiW1wi"
    "MjAwMzAxMDdcIiwyLFwiR2Vja29cIixcIk5ldHNjYXBlXCIsXCJNb3ppbGxhXCIsbnVsbCxudWxsLHRydWUsMTYsZmFsc2UsbnVs"
    "bCw1LHRydWUsdHJ1ZSxudWxsLDAsdHJ1ZSx0cnVlLHRydWUsdHJ1ZV0iXSxbLTQ5LCItIl0sWy02MywiLSJdLFstNjQsIlswLFwi"
    "XCIsW11dIl0sWy0yLCI0LFdUWkxJQUlhd2hoSDFWVUZieEtTQXFJb0x5Uk53VkZCVlFFVkZ4UThGOWYwOVFYSEJGSDhMREJkd1FR"
    "VUQyZllld2hTU1RaREl6ZCtudTJ2N2YwL2ZlWVJJVzBmLzcvWCsvLzYsMjY0ODExODU3NyJdLFstNSwiLSJdLFstNiwie1wid1wi"
    "OltcIjBcIixcInYzNzdcIixcImNiSnNvblBcIixcIl9fY29yZS1qc19zaGFyZWRfX1wiLFwiY2Jfd2luZG93X2xvZ2dlclwiLFwi"
    "X19TRU5UUllfX1wiLFwic2V0SW1tZWRpYXRlXCIsXCJjbGVhckltbWVkaWF0ZVwiLFwiQ2hhcmdlYmVlXCIsXCJpc0FkQmxvY2tB"
    "Y3RpdmVcIixcIndlYnBhY2tDaHVua2NyZWF0b3Jfd2ViXCIsXCJab25lXCIsXCJfX3pvbmVfc3ltYm9sX19Qcm9taXNlXCIsXCJf"
    "X3pvbmVfc3ltYm9sX19mZXRjaFwiLFwiX196b25lX3N5bWJvbF9fc2V0VGltZW91dFwiLFwiX196b25lX3N5bWJvbF9fY2xlYXJU"
    "aW1lb3V0XCIsXCJfX3pvbmVfc3ltYm9sX19zZXRJbnRlcnZhbFwiLFwiX196b25lX3N5bWJvbF9fY2xlYXJJbnRlcnZhbFwiLFwi"
    "X196b25lX3N5bWJvbF9fc2V0SW1tZWRpYXRlXCIsXCJfX3pvbmVfc3ltYm9sX19jbGVhckltbWVkaWF0ZVwiLFwiX196b25lX3N5"
    "bWJvbF9fcmVxdWVzdEFuaW1hdGlvbkZyYW1lXCIsXCJfX3pvbmVfc3ltYm9sX19jYW5jZWxBbmltYXRpb25GcmFtZVwiLFwiX196"
    "b25lX3N5bWJvbF9fd2Via2l0UmVxdWVzdEFuaW1hdGlvbkZyYW1lXCIsXCJfX3pvbmVfc3ltYm9sX193ZWJraXRDYW5jZWxBbmlt"
    "YXRpb25GcmFtZVwiLFwiX196b25lX3N5bWJvbF9fYWxlcnRcIixcIl9fem9uZV9zeW1ib2xfX3Byb21wdFwiLFwiX196b25lX3N5"
    "bWJvbF9fY29uZmlybVwiLFwiX196b25lX3N5bWJvbF9fTXV0YXRpb25PYnNlcnZlclwiLFwiX196b25lX3N5bWJvbF9fV2ViS2l0"
    "TXV0YXRpb25PYnNlcnZlclwiLFwiX196b25lX3N5bWJvbF9fSW50ZXJzZWN0aW9uT2JzZXJ2ZXJcIixcIl9fem9uZV9zeW1ib2xf"
    "X0ZpbGVSZWFkZXJcIixcIl9fem9uZV9zeW1ib2xfX29ub25wYWdlcmV2ZWFscGF0Y2hlZFwiLFwiX196b25lX3N5bWJvbF9fb25v"
    "bnNlYXJjaHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25hcHBpbnN0YWxsZWRwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9s"
    "X19vbm9uYmVmb3JlaW5zdGFsbHByb21wdHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25hYm9ydHBhdGNoZWRcIixcIl9f"
    "em9uZV9zeW1ib2xfX29ub25iZWZvcmVpbnB1dHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25iZWZvcmVtYXRjaHBhdGNo"
    "ZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25iZWZvcmV0b2dnbGVwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uYmx1cnBh"
    "dGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jYW5jZWxwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uY2FucGxheXBh"
    "dGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jYW5wbGF5dGhyb3VnaHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25j"
    "aGFuZ2VwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uY2xpY2twYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uY2xv"
    "c2VwYXRjaGVkXCIsXCJfX3pvbmVfc3ltYm9sX19vbm9uY29tbWFuZHBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jb250"
    "ZW50dmlzaWJpbGl0eWF1dG9zdGF0ZWNoYW5nZXBhdGNoZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jb250ZXh0bG9zdHBhdGNo"
    "ZWRcIixcIl9fem9uZV9zeW1ib2xfX29ub25jb250ZXh0bWVudXBhdGNoZWRcIl0sXCJuXCI6W10sXCJkXCI6W119Il0sWy01NCwi"
    "e1wiaFwiOltcIl8zXCIsXCIyODcyODk5MzIwXCIsXCI0MTA0MzE2MjNcIixcIjI2MzkyMjI0NjhcIixcIl8xXCIsXCI4OTY0Nzc3"
    "MTZcIl0sXCJkXCI6W10sXCJiXCI6W1wiXzFcIixcIjEwNjQ4MjI2ODdcIixcIjQxMDQzMTYyM1wiLFwiMjYzOTIyMjQ2OFwiXSxc"
    "InNcIjoxfSJdLFstNTcsIldFMFplRXRMV0VBWGZGd1pFVkZOVFVsS0F4WVdYRXhXV3hkYlVFTktYRmhLVWtBWFdsWlVGa3BCU1Ja"
    "UUZsMWZBUXRhRFZ4ZkR3d0tEMXdOWFZ4Y0R3a1BDUWdMQVFsYldnRUpEQUVCRjFOS0JsQmRCQWdORFE0S0F3Z0RBQUFLQ1FvUUZW"
    "aE5HVXNaRVZGTlRVbEtBeFlXWEV4V1d4ZGJVRU5LWEZoS1VrQVhXbFpVRmtwQlNSWlFGbDFmQVF0YURWeGZEd3dLRDF3TlhWeGNE"
    "d2tQQ1FnTEFRbGJXZ0VKREFFQkYxTktCbEJkQkFnTkRRNEtBd2dEQ0FrTUNBb1BFQlZZVFJsUlRVMUpTZ01XRmx4TVZsc1hXMUJE"
    "U2x4WVNsSkFGMXBXVkJaS1FVa1dVQlpkWHdFTFdnMWNYdzhNQ2c5Y0RWMWNYQThKRHdrSUN3RUpXdz09Il0sWy01OCwiLSJdLFst"
    "NzAsIi0iXSxbLTcyLCJFeFU9Il0sWy03NCwiMCwwIl0sWy03NSwie30uc29tZUZ1bmMgaXMgbm90IGEgZnVuY3Rpb24iXSxbImJu"
    "Y2giLDk0XSxbLTIwLCItIl0sWy0zMiwiMCJdLFstNTIsIjEyLDM0MjQ2NjUzNjgiXSxbLTY2LCJnZW9sb2NhdGlvbixjaHVhZnVs"
    "bHZlcnNpb25saXN0LGNyb3Nzb3JpZ2luaXNvbGF0ZWQsc2NyZWVud2FrZWxvY2ssb25kZXZpY2VzcGVlY2hyZWNvZ25pdGlvbix0"
    "cmFuc2xhdG9yLHB1YmxpY2tleWNyZWRlbnRpYWxzZ2V0LHNoYXJlZHN0b3JhZ2VzZWxlY3R1cmwsY2h1YWFyY2gsYmx1ZXRvb3Ro"
    "LGNvbXB1dGVwcmVzc3VyZSxjaHByZWZlcnNyZWR1Y2VkdHJhbnNwYXJlbmN5LGRlZmVycmVkZmV0Y2gsdXNiLGNoc2F2ZWRhdGEs"
    "cHVibGlja2V5Y3JlZGVudGlhbHNjcmVhdGUsc2hhcmVkc3RvcmFnZSxkZWZlcnJlZGZldGNobWluaW1hbCxydW5hZGF1Y3Rpb24s"
    "Y2hkb3dubGluayxjaHVhZm9ybWZhY3RvcnMsb3RwY3JlZGVudGlhbHMscGF5bWVudCxjaHVhLGNodWFtb2RlbCxjaGVjdCxhdXRv"
    "cGxheSxjYW1lcmEsbGFuZ3VhZ2VkZXRlY3Rvcixwcml2YXRlc3RhdGV0b2tlbmlzc3VhbmNlLGRpZ2l0YWxjcmVkZW50aWFsc2dl"
    "dCxhY2NlbGVyb21ldGVyLGNodWFwbGF0Zm9ybXZlcnNpb24saWRsZWRldGVjdGlvbixwcml2YXRlYWdncmVnYXRpb24saW50ZXJl"
    "c3Rjb2hvcnQsY2h2aWV3cG9ydGhlaWdodCxjYXB0dXJlZHN1cmZhY2Vjb250cm9sLGxvY2FsZm9udHMsY2h1YXBsYXRmb3JtLG1p"
    "ZGksY2h1YWZ1bGx2ZXJzaW9uLHhyc3BhdGlhbHRyYWNraW5nLGNsaXBib2FyZHJlYWQsZ2FtZXBhZCxkaXNwbGF5Y2FwdHVyZSxr"
    "ZXlib2FyZG1hcCxqb2luYWRpbnRlcmVzdGdyb3VwLGFyaWFub3RpZnksbG9jYWxuZXR3b3JrLGNodWFoaWdoZW50cm9weXZhbHVl"
    "cyxjaHdpZHRoLGNocHJlZmVyc3JlZHVjZWRtb3Rpb24sYnJvd3Npbmd0b3BpY3MsZW5jcnlwdGVkbWVkaWEsbG9jYWxuZXR3b3Jr"
    "YWNjZXNzLGd5cm9zY29wZSxzZXJpYWwsY2hydHQsY2h1YW1vYmlsZSx3aW5kb3dtYW5hZ2VtZW50LHVubG9hZCxjaGRwcixjaHBy"
    "ZWZlcnNjb2xvcnNjaGVtZSxjaHVhd293NjQsZnVsbHNjcmVlbixpZGVudGl0eWNyZWRlbnRpYWxzZ2V0LHByaXZhdGVzdGF0ZXRv"
    "a2VucmVkZW1wdGlvbixoaWQsc3VtbWFyaXplcixjaHVhYml0bmVzcyxzdG9yYWdlYWNjZXNzLHN5bmN4aHIsY2hkZXZpY2VtZW1v"
    "cnksY2h2aWV3cG9ydHdpZHRoLHBpY3R1cmVpbnBpY3R1cmUsbG9vcGJhY2tuZXR3b3JrLG1hZ25ldG9tZXRlcixjbGlwYm9hcmR3"
    "cml0ZSxtaWNyb3Bob25lIl0sWyJkZGIiLCIwLDQsMCwwLDAsMSwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCww"
    "LDAsMCwwLDAsMCwwLDAsMCwwLDAsMSwwLDAsMCwwLDAsMCwxLDEsMzEsMCw2LDAsMSwwLDAsMTQsMCwwLDAsMCwwLDAsMCwwLDEs"
    "MCwwLDAsMCwwLDAsMCwwLDEsMCwyLDEsMCwwLDAiXSxbImNiIiwiMCwwLDAsMCwwLDAsMCwwLDAsNywwLDAsNiwwLDAsMCwwLDAs"
    "MCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDEsMCwwLDEsMCwwLDAsMCwxLDAsMCww"
    "LDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDAsMCwwLDMsMCwwLDEsMCwwLDAsMCwxLDAsMCwwLDAsMSwxLDAsMCwwLDAs"
    "MCwwLDAsMCJdXQ=="
)


# Fresh request and session identifiers are added around the fingerprint template.
def collector_url(canonical: str) -> str:
    now = time.time()
    seconds = int(now)
    milliseconds = int(now * 1000)
    values = [
        ("id", "14473"),
        ("url", canonical),
        ("sf", "0"),
        ("tpi", ""),
        ("ch", "cheq4ppc"),
        ("uvid", ""),
        ("tsf", "0"),
        ("tsfmi", ""),
        ("tsfu", ""),
        ("cb", str(milliseconds)),
        ("hl", "2"),
        ("op", "0"),
        ("ag", "444981139"),
        ("rand", _digits(97)),
        ("fs", "756x441"),
        ("fst", "756x441"),
        ("np", "windows"),
        ("nv", "google inc."),
        ("ref", ""),
        ("ss", "800x600"),
        ("nc", "0"),
        ("at", ""),
        ("di", CHEQ_FINGERPRINT),
        ("dep", "0"),
        ("pre", "0"),
        ("sdd", ""),
        ("cri", "qR3HM3UpbF"),
        ("pto", str(secrets.randbelow(501) + 400)),
        ("ver", "66"),
        ("gac", "-"),
        ("mei", ""),
        ("ap", ""),
        ("fe", "1"),
        ("duid", _collector_id(seconds)),
        ("suid", _collector_id(seconds)),
        ("tuid", _collector_id(seconds)),
        ("sid", _collector_id(milliseconds)),
        ("fbc", "-"),
        ("gtm", "WyJvcGVuUGFnZSJd"),
        ("it", "64,420,109"),
        ("fbcl", "-"),
        ("gacl", "-"),
        ("gacsd", "-"),
        ("rtic", "-"),
        ("rtict", "-"),
        ("bgc", "-"),
        ("spa", "1"),
        ("urid", "0"),
        ("ab", ""),
        ("sck", "-"),
        ("io", "aGA2Oi1+aG02Og=="),
    ]
    return COLLECTOR_ENDPOINT + "?" + urlencode(values)


def _collector_id(timestamp: int) -> str:
    alphabet = string.ascii_letters + string.digits
    token = "".join(secrets.choice(alphabet) for _ in range(16))
    return f"1.{timestamp}.{token}"


def _digits(length: int) -> str:
    value = "".join(secrets.choice(string.digits) for _ in range(length))
    return ("1" if value[0] == "0" else value[0]) + value[1:]
