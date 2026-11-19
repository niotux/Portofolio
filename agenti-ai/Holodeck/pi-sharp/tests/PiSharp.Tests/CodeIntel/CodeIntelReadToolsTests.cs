using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiSharp.CodeIntel;
using PiSharp.CodeIntel.Git;
using PiSharp.CodeIntel.Lsp;
using PiSharp.CodeIntel.Query;

namespace PiSharp.Tests.CodeIntel;

internal static class FakeLsProgram
{
    public static readonly byte[] FakeLsSource = Convert.FromBase64String(
        "dXNpbmcgU3lzdGVtOwp1c2luZyBTeXN0ZW0uQ29sbGVjdGlvbnMuR2VuZXJpYzsKdXNpbmcgU3lz"
        + "dGVtLklPOwp1c2luZyBTeXN0ZW0uVGV4dDsKdXNpbmcgU3lzdGVtLlRleHQuSnNvbjsKdXNpbmcg"
        + "U3lzdGVtLlRleHQuSnNvbi5Ob2RlczsKCmNsYXNzIFByb2dyYW0KewogICAgc3RhdGljIHN0cmlu"
        + "ZyBMb2dGaWxlID0gIiI7CgogICAgc3RhdGljIHZvaWQgTG9nKHN0cmluZyBtc2cpCiAgICB7CiAg"
        + "ICAgICAgdHJ5IHsgRmlsZS5BcHBlbmRBbGxUZXh0KExvZ0ZpbGUsICQiW3tEYXRlVGltZS5VdGNO"
        + "b3c6T31dIHttc2d9XG4iKTsgfSBjYXRjaCB7IH0KICAgIH0KCiAgICBzdGF0aWMgU3RyZWFtIHN0"
        + "ZGluLCBzdGRvdXQ7CgogICAgc3RhdGljIHZvaWQgTWFpbihzdHJpbmdbXSBhcmdzKQogICAgewog"
        + "ICAgICAgIGlmIChhcmdzLkxlbmd0aCA+IDApIExvZ0ZpbGUgPSBhcmdzWzBdOwogICAgICAgIHN0"
        + "ZGluID0gQ29uc29sZS5PcGVuU3RhbmRhcmRJbnB1dCgpOwogICAgICAgIHN0ZG91dCA9IENvbnNv"
        + "bGUuT3BlblN0YW5kYXJkT3V0cHV0KCk7CiAgICAgICAgdHJ5CiAgICAgICAgewogICAgICAgICAg"
        + "ICB3aGlsZSAodHJ1ZSkKICAgICAgICAgICAgewogICAgICAgICAgICAgICAgaW50IGxlbmd0aCA9"
        + "IFJlYWRIZWFkZXIoKTsKICAgICAgICAgICAgICAgIGlmIChsZW5ndGggPCAwKSBicmVhazsKICAg"
        + "ICAgICAgICAgICAgIGJ5dGVbXSBib2R5ID0gUmVhZEJvZHkobGVuZ3RoKTsKICAgICAgICAgICAg"
        + "ICAgIHZhciBkb2MgPSBKc29uTm9kZS5QYXJzZShFbmNvZGluZy5VVEY4LkdldFN0cmluZyhib2R5"
        + "KSkhLkFzT2JqZWN0KCk7CiAgICAgICAgICAgICAgICBzdHJpbmc/IG1ldGhvZCA9IGRvY1sibWV0"
        + "aG9kIl0/LkdldFZhbHVlPHN0cmluZz4oKTsKICAgICAgICAgICAgICAgIHN0cmluZz8gaWQgPSBS"
        + "ZWFkSWQoZG9jKTsKICAgICAgICAgICAgICAgIExvZygkIm1ldGhvZD17bWV0aG9kID8/ICI/In0g"
        + "aWQ9e2lkID8/ICJudWxsIn0iKTsKCiAgICAgICAgICAgICAgICBpZiAobWV0aG9kID09IG51bGwg"
        + "JiYgaWQgIT0gbnVsbCkgY29udGludWU7CgogICAgICAgICAgICAgICAgLy8gRXh0cmFjdCB0aGUg"
        + "ZG9jdW1lbnQgVVJJIGZyb20gcGFyYW1zIGZvciBsb2NhdGlvbiByZXNwb25zZXMKICAgICAgICAg"
        + "ICAgICAgIHN0cmluZyBkb2NVcmkgPSBkb2NbInBhcmFtcyJdP1sidGV4dERvY3VtZW50Il0/WyJ1"
        + "cmkiXT8uR2V0VmFsdWU8c3RyaW5nPigpID8/ICJmaWxlOi8vL3Vua25vd24iOwoKICAgICAgICAg"
        + "ICAgICAgIGlmIChtZXRob2QgPT0gImluaXRpYWxpemUiKQogICAgICAgICAgICAgICAgewogICAg"
        + "ICAgICAgICAgICAgICAgIHZhciByZXN1bHQgPSBuZXcgSnNvbk9iamVjdAogICAgICAgICAgICAg"
        + "ICAgICAgIHsKICAgICAgICAgICAgICAgICAgICAgICAgWyJjYXBhYmlsaXRpZXMiXSA9IG5ldyBK"
        + "c29uT2JqZWN0CiAgICAgICAgICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAgICAg"
        + "ICAgICAgIFsiZGVmaW5pdGlvblByb3ZpZGVyIl0gPSB0cnVlLAogICAgICAgICAgICAgICAgICAg"
        + "ICAgICAgICAgWyJyZWZlcmVuY2VzUHJvdmlkZXIiXSA9IHRydWUsCiAgICAgICAgICAgICAgICAg"
        + "ICAgICAgICAgICBbImhvdmVyUHJvdmlkZXIiXSA9IHRydWUsCiAgICAgICAgICAgICAgICAgICAg"
        + "ICAgICAgICBbImRvY3VtZW50U3ltYm9sUHJvdmlkZXIiXSA9IHRydWUsCiAgICAgICAgICAgICAg"
        + "ICAgICAgICAgICAgICBbImRpYWdub3N0aWNQcm92aWRlciJdID0gbmV3IEpzb25PYmplY3QgeyBb"
        + "ImludGVyRmlsZURlcGVuZGVuY2llcyJdID0gZmFsc2UgfQogICAgICAgICAgICAgICAgICAgICAg"
        + "ICB9CiAgICAgICAgICAgICAgICAgICAgfTsKICAgICAgICAgICAgICAgICAgICBTZW5kUmVzcG9u"
        + "c2UoaWQsIHJlc3VsdCk7CiAgICAgICAgICAgICAgICAgICAgU2VuZE5vdGlmaWNhdGlvbigiJC9w"
        + "cm9ncmVzcyIsIG5ldyBKc29uT2JqZWN0CiAgICAgICAgICAgICAgICAgICAgewogICAgICAgICAg"
        + "ICAgICAgICAgICAgICBbInRva2VuIl0gPSAicmVhZHkiLAogICAgICAgICAgICAgICAgICAgICAg"
        + "ICBbInZhbHVlIl0gPSBuZXcgSnNvbk9iamVjdCB7IFsia2luZCJdID0gImVuZCIgfQogICAgICAg"
        + "ICAgICAgICAgICAgIH0pOwogICAgICAgICAgICAgICAgfQogICAgICAgICAgICAgICAgZWxzZSBp"
        + "ZiAobWV0aG9kID09ICJpbml0aWFsaXplZCIgfHwgbWV0aG9kID09ICJ0ZXh0RG9jdW1lbnQvZGlk"
        + "T3BlbiIpCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgLy8gbm90aWZpY2F0"
        + "aW9uOiBpZ25vcmUKICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgICAgIGVsc2UgaWYgKG1l"
        + "dGhvZCA9PSAidGV4dERvY3VtZW50L2RlZmluaXRpb24iKQogICAgICAgICAgICAgICAgewogICAg"
        + "ICAgICAgICAgICAgICAgIC8vIDYgbG9jYXRpb25zIGF0IDAtYmFzZWQgbGluZSAwIHNvIGNvdW50"
        + "PTUsIHRydW5jYXRlZD10cnVlCiAgICAgICAgICAgICAgICAgICAgdmFyIGxvY3MgPSBuZXcgSnNv"
        + "bkFycmF5KCk7CiAgICAgICAgICAgICAgICAgICAgZm9yIChpbnQgaSA9IDA7IGkgPCA2OyBpKysp"
        + "CiAgICAgICAgICAgICAgICAgICAgICAgIGxvY3MuQWRkKG5ldyBKc29uT2JqZWN0CiAgICAgICAg"
        + "ICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAgICAgICAgICAgIFsidXJpIl0gPSBk"
        + "b2NVcmksCiAgICAgICAgICAgICAgICAgICAgICAgICAgICBbInJhbmdlIl0gPSBuZXcgSnNvbk9i"
        + "amVjdAogICAgICAgICAgICAgICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgICAg"
        + "ICAgICAgICAgIFsic3RhcnQiXSA9IG5ldyBKc29uT2JqZWN0IHsgWyJsaW5lIl0gPSAwLCBbImNo"
        + "YXJhY3RlciJdID0gMCB9LAogICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgIFsiZW5kIl0g"
        + "PSBuZXcgSnNvbk9iamVjdCB7IFsibGluZSJdID0gMCwgWyJjaGFyYWN0ZXIiXSA9IDEwIH0KICAg"
        + "ICAgICAgICAgICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgICAgICAgICAgICAgfSk7CiAg"
        + "ICAgICAgICAgICAgICAgICAgU2VuZFJlc3BvbnNlKGlkLCBsb2NzKTsKICAgICAgICAgICAgICAg"
        + "IH0KICAgICAgICAgICAgICAgIGVsc2UgaWYgKG1ldGhvZCA9PSAidGV4dERvY3VtZW50L3JlZmVy"
        + "ZW5jZXMiKQogICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgIC8vIDMxIGxvY2F0"
        + "aW9ucyBzbyBjb3VudD0zMCwgdHJ1bmNhdGVkPXRydWUKICAgICAgICAgICAgICAgICAgICB2YXIg"
        + "bG9jcyA9IG5ldyBKc29uQXJyYXkoKTsKICAgICAgICAgICAgICAgICAgICBmb3IgKGludCBpID0g"
        + "MDsgaSA8IDMxOyBpKyspCiAgICAgICAgICAgICAgICAgICAgICAgIGxvY3MuQWRkKG5ldyBKc29u"
        + "T2JqZWN0CiAgICAgICAgICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAgICAgICAg"
        + "ICAgIFsidXJpIl0gPSBkb2NVcmksCiAgICAgICAgICAgICAgICAgICAgICAgICAgICBbInJhbmdl"
        + "Il0gPSBuZXcgSnNvbk9iamVjdAogICAgICAgICAgICAgICAgICAgICAgICAgICAgewogICAgICAg"
        + "ICAgICAgICAgICAgICAgICAgICAgICAgIFsic3RhcnQiXSA9IG5ldyBKc29uT2JqZWN0IHsgWyJs"
        + "aW5lIl0gPSBpLCBbImNoYXJhY3RlciJdID0gMCB9LAogICAgICAgICAgICAgICAgICAgICAgICAg"
        + "ICAgICAgIFsiZW5kIl0gPSBuZXcgSnNvbk9iamVjdCB7IFsibGluZSJdID0gaSwgWyJjaGFyYWN0"
        + "ZXIiXSA9IDUgfQogICAgICAgICAgICAgICAgICAgICAgICAgICAgfQogICAgICAgICAgICAgICAg"
        + "ICAgICAgICB9KTsKICAgICAgICAgICAgICAgICAgICBTZW5kUmVzcG9uc2UoaWQsIGxvY3MpOwog"
        + "ICAgICAgICAgICAgICAgfQogICAgICAgICAgICAgICAgZWxzZSBpZiAobWV0aG9kID09ICJ0ZXh0"
        + "RG9jdW1lbnQvaG92ZXIiKQogICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgIFNl"
        + "bmRSZXNwb25zZShpZCwgbmV3IEpzb25PYmplY3QKICAgICAgICAgICAgICAgICAgICB7CiAgICAg"
        + "ICAgICAgICAgICAgICAgICAgIFsiY29udGVudHMiXSA9IG5ldyBKc29uQXJyYXkKICAgICAgICAg"
        + "ICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgICAgICAgICAgbmV3IEpzb25PYmpl"
        + "Y3QgeyBbImtpbmQiXSA9ICJtYXJrZG93biIsIFsidmFsdWUiXSA9ICJmYWtlIGhvdmVyIiB9CiAg"
        + "ICAgICAgICAgICAgICAgICAgICAgIH0sCiAgICAgICAgICAgICAgICAgICAgICAgIFsicmFuZ2Ui"
        + "XSA9IG5ldyBKc29uT2JqZWN0CiAgICAgICAgICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAg"
        + "ICAgICAgICAgICAgICAgIFsic3RhcnQiXSA9IG5ldyBKc29uT2JqZWN0IHsgWyJsaW5lIl0gPSAx"
        + "LCBbImNoYXJhY3RlciJdID0gNSB9LAogICAgICAgICAgICAgICAgICAgICAgICAgICAgWyJlbmQi"
        + "XSA9IG5ldyBKc29uT2JqZWN0IHsgWyJsaW5lIl0gPSAxLCBbImNoYXJhY3RlciJdID0gMTAgfQog"
        + "ICAgICAgICAgICAgICAgICAgICAgICB9CiAgICAgICAgICAgICAgICAgICAgfSk7CiAgICAgICAg"
        + "ICAgICAgICB9CiAgICAgICAgICAgICAgICBlbHNlIGlmIChtZXRob2QgPT0gInRleHREb2N1bWVu"
        + "dC9kb2N1bWVudFN5bWJvbCIpCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAg"
        + "Ly8gMjEgc3ltYm9scyBzbyBjb3VudD0yMCwgdHJ1bmNhdGVkPXRydWU7IHN5bTAgYXQgMC1iYXNl"
        + "ZCBsaW5lIDEKICAgICAgICAgICAgICAgICAgICB2YXIgc3ltcyA9IG5ldyBKc29uQXJyYXkoKTsK"
        + "ICAgICAgICAgICAgICAgICAgICBmb3IgKGludCBpID0gMDsgaSA8IDIxOyBpKyspCiAgICAgICAg"
        + "ICAgICAgICAgICAgICAgIHN5bXMuQWRkKG5ldyBKc29uT2JqZWN0CiAgICAgICAgICAgICAgICAg"
        + "ICAgICAgIHsKICAgICAgICAgICAgICAgICAgICAgICAgICAgIFsibmFtZSJdID0gInN5bSIgKyBp"
        + "LAogICAgICAgICAgICAgICAgICAgICAgICAgICAgWyJraW5kIl0gPSAxMiwKICAgICAgICAgICAg"
        + "ICAgICAgICAgICAgICAgIFsicmFuZ2UiXSA9IG5ldyBKc29uT2JqZWN0CiAgICAgICAgICAgICAg"
        + "ICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgWyJzdGFydCJd"
        + "ID0gbmV3IEpzb25PYmplY3QgeyBbImxpbmUiXSA9IDAsIFsiY2hhcmFjdGVyIl0gPSAwIH0sCiAg"
        + "ICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgWyJlbmQiXSA9IG5ldyBKc29uT2JqZWN0IHsg"
        + "WyJsaW5lIl0gPSAxLCBbImNoYXJhY3RlciJdID0gMCB9CiAgICAgICAgICAgICAgICAgICAgICAg"
        + "ICAgICB9LAogICAgICAgICAgICAgICAgICAgICAgICAgICAgWyJzZWxlY3Rpb25SYW5nZSJdID0g"
        + "bmV3IEpzb25PYmplY3QKICAgICAgICAgICAgICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAg"
        + "ICAgICAgICAgICAgICAgICAgICBbInN0YXJ0Il0gPSBuZXcgSnNvbk9iamVjdCB7IFsibGluZSJd"
        + "ID0gMCwgWyJjaGFyYWN0ZXIiXSA9IDAgfSwKICAgICAgICAgICAgICAgICAgICAgICAgICAgICAg"
        + "ICBbImVuZCJdID0gbmV3IEpzb25PYmplY3QgeyBbImxpbmUiXSA9IDAsIFsiY2hhcmFjdGVyIl0g"
        + "PSA1IH0KICAgICAgICAgICAgICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgICAgICAgICAg"
        + "ICAgfSk7CiAgICAgICAgICAgICAgICAgICAgU2VuZFJlc3BvbnNlKGlkLCBzeW1zKTsKICAgICAg"
        + "ICAgICAgICAgIH0KICAgICAgICAgICAgICAgIGVsc2UgaWYgKG1ldGhvZCA9PSAidGV4dERvY3Vt"
        + "ZW50L2RpYWdub3N0aWMiKQogICAgICAgICAgICAgICAgewogICAgICAgICAgICAgICAgICAgIC8v"
        + "IDIxIGVycm9yIGRpYWdub3N0aWNzOiAwLWJhc2VkIGxpbmVzIDAtNCBzbyAxLWJhc2VkIHN0YXJ0"
        + "X2xpbmU9MSwgZW5kX2xpbmU9NQogICAgICAgICAgICAgICAgICAgIHZhciBkaWFncyA9IG5ldyBK"
        + "c29uQXJyYXkoKTsKICAgICAgICAgICAgICAgICAgICBmb3IgKGludCBpID0gMDsgaSA8IDIxOyBp"
        + "KyspCiAgICAgICAgICAgICAgICAgICAgICAgIGRpYWdzLkFkZChuZXcgSnNvbk9iamVjdAogICAg"
        + "ICAgICAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgICAgICAgICBbInJhbmdl"
        + "Il0gPSBuZXcgSnNvbk9iamVjdAogICAgICAgICAgICAgICAgICAgICAgICAgICAgewogICAgICAg"
        + "ICAgICAgICAgICAgICAgICAgICAgICAgIFsic3RhcnQiXSA9IG5ldyBKc29uT2JqZWN0IHsgWyJs"
        + "aW5lIl0gPSAwLCBbImNoYXJhY3RlciJdID0gMCB9LAogICAgICAgICAgICAgICAgICAgICAgICAg"
        + "ICAgICAgIFsiZW5kIl0gPSBuZXcgSnNvbk9iamVjdCB7IFsibGluZSJdID0gNCwgWyJjaGFyYWN0"
        + "ZXIiXSA9IDAgfQogICAgICAgICAgICAgICAgICAgICAgICAgICAgfSwKICAgICAgICAgICAgICAg"
        + "ICAgICAgICAgICAgIFsic2V2ZXJpdHkiXSA9IDEsCiAgICAgICAgICAgICAgICAgICAgICAgICAg"
        + "ICBbImNvZGUiXSA9ICJGQUtFIiArIGksCiAgICAgICAgICAgICAgICAgICAgICAgICAgICBbInNv"
        + "dXJjZSJdID0gImZha2UiLAogICAgICAgICAgICAgICAgICAgICAgICAgICAgWyJtZXNzYWdlIl0g"
        + "PSAiZmFrZSBlcnJvciAiICsgaQogICAgICAgICAgICAgICAgICAgICAgICB9KTsKICAgICAgICAg"
        + "ICAgICAgICAgICBTZW5kUmVzcG9uc2UoaWQsIG5ldyBKc29uT2JqZWN0CiAgICAgICAgICAgICAg"
        + "ICAgICAgewogICAgICAgICAgICAgICAgICAgICAgICBbIml0ZW1zIl0gPSBkaWFncwogICAgICAg"
        + "ICAgICAgICAgICAgIH0pOwogICAgICAgICAgICAgICAgfQogICAgICAgICAgICAgICAgZWxzZSBp"
        + "ZiAobWV0aG9kID09ICJleGl0IikKICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAg"
        + "ICBicmVhazsKICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgICAgIGVsc2UgaWYgKGlkICE9"
        + "IG51bGwpCiAgICAgICAgICAgICAgICB7CiAgICAgICAgICAgICAgICAgICAgU2VuZEVycm9yKGlk"
        + "LCBuZXcgSnNvbk9iamVjdAogICAgICAgICAgICAgICAgICAgIHsKICAgICAgICAgICAgICAgICAg"
        + "ICAgICAgWyJjb2RlIl0gPSAtMzI2MDEsCiAgICAgICAgICAgICAgICAgICAgICAgIFsibWVzc2Fn"
        + "ZSJdID0gIk1ldGhvZCBub3QgZm91bmQ6ICIgKyBtZXRob2QKICAgICAgICAgICAgICAgICAgICB9"
        + "KTsKICAgICAgICAgICAgICAgIH0KICAgICAgICAgICAgfQogICAgICAgIH0KICAgICAgICBjYXRj"
        + "aCAoRXhjZXB0aW9uIGV4KQogICAgICAgIHsKICAgICAgICAgICAgTG9nKCJGQVRBTDogIiArIGV4"
        + "KTsKICAgICAgICAgICAgdGhyb3c7CiAgICAgICAgfQogICAgfQoKICAgIHN0YXRpYyBzdHJpbmc/"
        + "IFJlYWRJZChKc29uT2JqZWN0IGRvYykKICAgIHsKICAgICAgICBpZiAoZG9jWyJpZCJdIGlzIG51"
        + "bGwpIHJldHVybiBudWxsOwogICAgICAgIHZhciBpZE5vZGUgPSBkb2NbImlkIl07CiAgICAgICAg"
        + "aWYgKGlkTm9kZSBpcyBKc29uVmFsdWUgdiAmJiB2LlRyeUdldFZhbHVlPHN0cmluZz4ob3V0IHZh"
        + "ciBzKSkgcmV0dXJuIHM7CiAgICAgICAgaWYgKGlkTm9kZSBpcyBKc29uVmFsdWUgdjIgJiYgdjIu"
        + "VHJ5R2V0VmFsdWU8bG9uZz4ob3V0IHZhciBsKSkgcmV0dXJuIGwuVG9TdHJpbmcoKTsKICAgICAg"
        + "ICBpZiAoaWROb2RlIGlzIEpzb25WYWx1ZSB2MyAmJiB2My5UcnlHZXRWYWx1ZTxpbnQ+KG91dCB2"
        + "YXIgaSkpIHJldHVybiBpLlRvU3RyaW5nKCk7CiAgICAgICAgcmV0dXJuIG51bGw7CiAgICB9Cgog"
        + "ICAgc3RhdGljIGludCBSZWFkSGVhZGVyKCkKICAgIHsKICAgICAgICB2YXIgaGVhZGVyID0gbmV3"
        + "IExpc3Q8Ynl0ZT4oKTsKICAgICAgICB3aGlsZSAodHJ1ZSkKICAgICAgICB7CiAgICAgICAgICAg"
        + "IGludCBiID0gc3RkaW4uUmVhZEJ5dGUoKTsKICAgICAgICAgICAgaWYgKGIgPCAwKSByZXR1cm4g"
        + "LTE7CiAgICAgICAgICAgIGhlYWRlci5BZGQoKGJ5dGUpYik7CiAgICAgICAgICAgIGlmIChoZWFk"
        + "ZXIuQ291bnQgPj0gNCAmJiBoZWFkZXJbXjRdID09IDEzICYmIGhlYWRlclteM10gPT0gMTAgJiYg"
        + "aGVhZGVyW14yXSA9PSAxMyAmJiBoZWFkZXJbXjFdID09IDEwKSBicmVhazsKICAgICAgICB9CiAg"
        + "ICAgICAgaW50IGxlbmd0aCA9IDA7CiAgICAgICAgZm9yZWFjaCAodmFyIGxpbmUgaW4gRW5jb2Rp"
        + "bmcuQVNDSUkuR2V0U3RyaW5nKGhlYWRlci5Ub0FycmF5KCkpLlNwbGl0KCdcbicpKQogICAgICAg"
        + "ICAgICBpZiAobGluZS5UcmltKCkuU3RhcnRzV2l0aCgiQ29udGVudC1MZW5ndGg6IikpCiAgICAg"
        + "ICAgICAgICAgICBsZW5ndGggPSBpbnQuUGFyc2UobGluZS5UcmltKClbMTUuLl0uVHJpbSgpKTsK"
        + "ICAgICAgICByZXR1cm4gbGVuZ3RoOwogICAgfQoKICAgIHN0YXRpYyBieXRlW10gUmVhZEJvZHko"
        + "aW50IGxlbmd0aCkKICAgIHsKICAgICAgICB2YXIgYm9keSA9IG5ldyBieXRlW2xlbmd0aF07CiAg"
        + "ICAgICAgaW50IHJlYWQgPSAwOwogICAgICAgIHdoaWxlIChyZWFkIDwgbGVuZ3RoKQogICAgICAg"
        + "IHsKICAgICAgICAgICAgaW50IG4gPSBzdGRpbi5SZWFkKGJvZHksIHJlYWQsIGxlbmd0aCAtIHJl"
        + "YWQpOwogICAgICAgICAgICBpZiAobiA8PSAwKSBicmVhazsKICAgICAgICAgICAgcmVhZCArPSBu"
        + "OwogICAgICAgIH0KICAgICAgICByZXR1cm4gYm9keTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBT"
        + "ZW5kUmVzcG9uc2Uoc3RyaW5nPyBpZCwgSnNvbk5vZGUgcmVzdWx0KQogICAgewogICAgICAgIFNl"
        + "bmQobmV3IEpzb25PYmplY3QKICAgICAgICB7CiAgICAgICAgICAgIFsianNvbnJwYyJdID0gIjIu"
        + "MCIsCiAgICAgICAgICAgIFsiaWQiXSA9IGlkICE9IG51bGwgPyBKc29uVmFsdWUuQ3JlYXRlKGlk"
        + "KSA6IG51bGwsCiAgICAgICAgICAgIFsicmVzdWx0Il0gPSByZXN1bHQKICAgICAgICB9KTsKICAg"
        + "IH0KCiAgICBzdGF0aWMgdm9pZCBTZW5kRXJyb3Ioc3RyaW5nPyBpZCwgSnNvbk9iamVjdCBlcnJv"
        + "cikKICAgIHsKICAgICAgICBTZW5kKG5ldyBKc29uT2JqZWN0CiAgICAgICAgewogICAgICAgICAg"
        + "ICBbImpzb25ycGMiXSA9ICIyLjAiLAogICAgICAgICAgICBbImlkIl0gPSBpZCAhPSBudWxsID8g"
        + "SnNvblZhbHVlLkNyZWF0ZShpZCkgOiBudWxsLAogICAgICAgICAgICBbImVycm9yIl0gPSBlcnJv"
        + "cgogICAgICAgIH0pOwogICAgfQoKICAgIHN0YXRpYyB2b2lkIFNlbmROb3RpZmljYXRpb24oc3Ry"
        + "aW5nIG1ldGhvZCwgSnNvbk5vZGU/IEBwYXJhbXMpCiAgICB7CiAgICAgICAgU2VuZChuZXcgSnNv"
        + "bk9iamVjdAogICAgICAgIHsKICAgICAgICAgICAgWyJqc29ucnBjIl0gPSAiMi4wIiwKICAgICAg"
        + "ICAgICAgWyJtZXRob2QiXSA9IG1ldGhvZCwKICAgICAgICAgICAgWyJwYXJhbXMiXSA9IEBwYXJh"
        + "bXMKICAgICAgICB9KTsKICAgIH0KCiAgICBzdGF0aWMgdm9pZCBTZW5kKEpzb25PYmplY3QgcGF5"
        + "bG9hZCkKICAgIHsKICAgICAgICBzdHJpbmcganNvbiA9IHBheWxvYWQuVG9Kc29uU3RyaW5nKCk7"
        + "CiAgICAgICAgYnl0ZVtdIGJ5dGVzID0gRW5jb2RpbmcuVVRGOC5HZXRCeXRlcyhqc29uKTsKICAg"
        + "ICAgICBzdHJpbmcgaGVhZGVyID0gIkNvbnRlbnQtTGVuZ3RoOiAiICsgYnl0ZXMuTGVuZ3RoICsg"
        + "IlxyXG5cclxuIjsKICAgICAgICBieXRlW10gYWxsID0gRW5jb2RpbmcuQVNDSUkuR2V0Qnl0ZXMo"
        + "aGVhZGVyKTsKICAgICAgICBieXRlW10gZnVsbCA9IG5ldyBieXRlW2FsbC5MZW5ndGggKyBieXRl"
        + "cy5MZW5ndGhdOwogICAgICAgIEFycmF5LkNvcHkoYWxsLCAwLCBmdWxsLCAwLCBhbGwuTGVuZ3Ro"
        + "KTsKICAgICAgICBBcnJheS5Db3B5KGJ5dGVzLCAwLCBmdWxsLCBhbGwuTGVuZ3RoLCBieXRlcy5M"
        + "ZW5ndGgpOwogICAgICAgIHN0ZG91dC5Xcml0ZShmdWxsLCAwLCBmdWxsLkxlbmd0aCk7CiAgICAg"
        + "ICAgc3Rkb3V0LkZsdXNoKCk7CiAgICAgICAgTG9nKCJzZW50ICIgKyBqc29uLkxlbmd0aCArICIg"
        + "Ynl0ZXMiKTsKICAgIH0KfQo="
        );







    public static string Source => Encoding.UTF8.GetString(FakeLsSource);

    public static string BuildOnce(string dir)
    {
        var root = Path.Combine(dir, $"fake-ls-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Program.cs"), Source);
        File.WriteAllText(Path.Combine(root, "fake-ls.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <Nullable>disable</Nullable>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "    <InvariantGlobalization>true</InvariantGlobalization>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n");
        var psi = new ProcessStartInfo("dotnet", $"build {root} -c Release -o {Path.Combine(root, "out")} -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi)!;
        var outTxt = p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(240_000)) { p.Kill(true); throw new InvalidOperationException("fake-ls build timed out"); }
        var dll = Path.Combine(root, "out", "fake-ls.dll");
        if (p.ExitCode != 0) throw new InvalidOperationException($"fake-ls build failed (exit {p.ExitCode}): stdout={outTxt} stderr={err}");
        if (!File.Exists(dll)) throw new InvalidOperationException($"fake-ls build exited 0 but {dll} missing; stdout={outTxt} stderr={err}");
        return dll;
    }
}

internal static class ChunkedLsProgram
{
    public static readonly byte[] ChunkedLsSource = Convert.FromBase64String(
        "dXNpbmcgU3lzdGVtLlRleHQ7CnVzaW5nIFN5c3RlbS5UZXh0Lkpzb247CnVzaW5nIFN5c3Rl"
        + "bS5UZXh0Lkpzb24uTm9kZXM7CgpjbGFzcyBQcm9ncmFtCnsKICAgIHN0YXRpYyB2b2lkIE1h"
        + "aW4oKQogICAgewogICAgICAgIHRyeQogICAgICAgIHsKICAgICAgICAgICAgUnVuKCk7CiAg"
        + "ICAgICAgfQogICAgICAgIGNhdGNoIChFeGNlcHRpb24gZXgpCiAgICAgICAgewogICAgICAg"
        + "ICAgICB0cnkgeyBGaWxlLkFwcGVuZEFsbFRleHQoIi90bXAvY2h1bmtlZC1scy1lcnIudHh0"
        + "IiwgZXguVG9TdHJpbmcoKSArICJcbiIpOyB9IGNhdGNoIHsgfQogICAgICAgICAgICB0aHJv"
        + "dzsKICAgICAgICB9CiAgICB9CgogICAgc3RhdGljIHZvaWQgUnVuKCkKICAgIHsKICAgICAg"
        + "ICB2YXIgc3RkaW4gPSBDb25zb2xlLk9wZW5TdGFuZGFyZElucHV0KCk7CiAgICAgICAgdmFy"
        + "IHN0ZG91dCA9IENvbnNvbGUuT3BlblN0YW5kYXJkT3V0cHV0KCk7CiAgICAgICAgdmFyIGhl"
        + "YWRlciA9IG5ldyBMaXN0PGJ5dGU+KCk7CiAgICAgICAgd2hpbGUgKHRydWUpCiAgICAgICAg"
        + "ewogICAgICAgICAgICBpbnQgYiA9IHN0ZGluLlJlYWRCeXRlKCk7CiAgICAgICAgICAgIGlm"
        + "IChiIDwgMCkgcmV0dXJuOwogICAgICAgICAgICBoZWFkZXIuQWRkKChieXRlKWIpOwogICAg"
        + "ICAgICAgICBpZiAoaGVhZGVyLkNvdW50ID49IDQgJiYgaGVhZGVyW140XSA9PSAxMyAmJiBo"
        + "ZWFkZXJbXjNdID09IDEwICYmIGhlYWRlclteMl0gPT0gMTMgJiYgaGVhZGVyW14xXSA9PSAx"
        + "MCkgYnJlYWs7CiAgICAgICAgfQogICAgICAgIGludCBsZW5ndGggPSAwOwogICAgICAgIGZv"
        + "cmVhY2ggKHZhciBsaW5lIGluIEVuY29kaW5nLkFTQ0lJLkdldFN0cmluZyhoZWFkZXIuVG9B"
        + "cnJheSgpKS5TcGxpdCgnXG4nKSkKICAgICAgICAgICAgaWYgKGxpbmUuVHJpbSgpLlN0YXJ0"
        + "c1dpdGgoIkNvbnRlbnQtTGVuZ3RoOiIpKSBsZW5ndGggPSBpbnQuUGFyc2UobGluZS5Ucmlt"
        + "KClbMTUuLl0pOwogICAgICAgIHZhciBib2R5ID0gbmV3IGJ5dGVbbGVuZ3RoXTsKICAgICAg"
        + "ICBpbnQgcmVhZCA9IDA7CiAgICAgICAgd2hpbGUgKHJlYWQgPCBsZW5ndGgpCiAgICAgICAg"
        + "ewogICAgICAgICAgICBpbnQgbmIgPSBzdGRpbi5SZWFkQnl0ZSgpOwogICAgICAgICAgICBp"
        + "ZiAobmIgPCAwKSByZXR1cm47CiAgICAgICAgICAgIGJvZHlbcmVhZCsrXSA9IChieXRlKW5i"
        + "OwogICAgICAgIH0KICAgICAgICB2YXIgZG9jID0gSnNvbk5vZGUuUGFyc2UoRW5jb2Rpbmcu"
        + "VVRGOC5HZXRTdHJpbmcoYm9keSkpIS5Bc09iamVjdCgpOwogICAgICAgIHZhciBpZE5vZGUg"
        + "PSBkb2NbImlkIl07CiAgICAgICAgdmFyIGlkID0gaWROb2RlICE9IG51bGwgJiYgaWROb2Rl"
        + "LkFzVmFsdWUoKS5HZXRWYWx1ZUtpbmQoKSA9PSBKc29uVmFsdWVLaW5kLlN0cmluZyA/IChz"
        + "dHJpbmcpaWROb2RlISA6IG51bGw7CiAgICAgICAgc3RyaW5nIGJpZyA9ICJ4IiArIG5ldyBz"
        + "dHJpbmcoJ3knLCA0MDk2KTsKICAgICAgICB2YXIgcGF5bG9hZCA9IG5ldyBKc29uT2JqZWN0"
        + "IHsgWyJqc29ucnBjIl0gPSAiMi4wIiwgWyJpZCJdID0gaWQgIT0gIm51bGwiID8gSnNvblZh"
        + "bHVlLkNyZWF0ZShpZCkgOiBudWxsLCBbInJlc3VsdCJdID0gYmlnIH07CiAgICAgICAgc3Ry"
        + "aW5nIHJlcGx5ID0gcGF5bG9hZC5Ub0pzb25TdHJpbmcoKTsKICAgICAgICB2YXIgYnl0ZXMg"
        + "PSBFbmNvZGluZy5VVEY4LkdldEJ5dGVzKHJlcGx5KTsKICAgICAgICB2YXIgaGFsZiA9IGJ5"
        + "dGVzLkxlbmd0aCAvIDI7CiAgICAgICAgc3Rkb3V0LldyaXRlKEVuY29kaW5nLkFTQ0lJLkdl"
        + "dEJ5dGVzKCJDb250ZW50LUxlbmd0aDogIiArIGJ5dGVzLkxlbmd0aCArICJcclxuXHJcbiIp"
        + "KTsKICAgICAgICBzdGRvdXQuV3JpdGUoYnl0ZXMsIDAsIGhhbGYpOwogICAgICAgIHN0ZG91"
        + "dC5GbHVzaCgpOwogICAgICAgIFRocmVhZC5TbGVlcCgxNTApOwogICAgICAgIHN0ZG91dC5X"
        + "cml0ZShieXRlcywgaGFsZiwgYnl0ZXMuTGVuZ3RoIC0gaGFsZik7CiAgICAgICAgc3Rkb3V0"
        + "LkZsdXNoKCk7CiAgICB9Cn0K"
        );


}

public class FakeLsReadToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci2-fake-{Guid.NewGuid():N}");
    private readonly List<Process> _fakeProcesses = new();
    private string _fakeDll;

    public FakeLsReadToolTests()
    {
        Directory.CreateDirectory(_root);
        _fakeDll = FakeLsProgram.BuildOnce(_root);
    }

    public void Dispose()
    {
        foreach (var p in _fakeProcesses)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); p.WaitForExit(5000); p.Dispose(); }
            catch { }
        }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeGitRepo(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var lines = new List<string> { "using System;", "public class Program", "{" };
        for (var i = 0; i < 40; i++) lines.Add("    private const int C" + i + " = " + i + ";");
        lines.Add("}");
        File.WriteAllText(Path.Combine(dir, "Program.cs"), string.Join("\n", lines));
        void Git(params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(60_000)) throw new InvalidOperationException("git timed out");
            if (p.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(' ', args) + " failed: " + err);
        }
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "test@example.com");
        Git("config", "user.name", "ci2 test");
        Git("add", "Program.cs");
        Git("commit", "-q", "-m", "init");
        return dir;
    }

    private (CodeIntelligence qi, LiveWorkspace ws) Attach(string seat, string repo)
    {
        var opts = Options.Create(new CodeIntelOptions
        {
            WorkspaceRoot = Path.Combine(_root, "workspaces"),
            LsKind = "fake",
            ReadyTimeout = TimeSpan.FromSeconds(15),
        });
        var svc = Options.Create(new CodeIntelServiceOptions
        {
            MaxLive = 3,
            IdleTtl = TimeSpan.FromMinutes(15),
            StartProcess = _ =>
            {
                var logFile = Path.Combine(_root, $"fake-ls-{Guid.NewGuid():N}.log");
                File.Create(logFile).Dispose();
                var psi = new ProcessStartInfo("dotnet")
                {
                    ArgumentList = { _fakeDll, logFile },
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                var p = Process.Start(psi)!;
                _fakeProcesses.Add(p);
                return p;
            },
        });
        var wm = new WorkspaceManager(opts, svc, NullLogger<WorkspaceManager>.Instance);
        var qi = new CodeIntelligence(wm);
        var ws = wm.AttachAsync(seat, repo, "main", null, CancellationToken.None).GetAwaiter().GetResult();
        var ready = ws.Ls.WaitReadyAsync(CancellationToken.None);
#pragma warning disable xUnit1031 // WaitReadyAsync has no await overload
        Assert.True(ready.GetAwaiter().GetResult(), "fake LS did not report ready");
#pragma warning restore xUnit1031
        ws.Ls.OpenFileAsync(
            ShadowWorkspace.FileUriOf(Path.Combine(ws.Worktree, "Program.cs")),
            CancellationToken.None).GetAwaiter().GetResult();
        return (qi, ws);
    }

    [Fact]
    public async Task Definition_LimitsTo5_WithTruncationFlag_AndContextSelectors()
    {
        var (qi, ws) = Attach("coder-06", MakeGitRepo("repo1"));
        try
        {
            var r = await qi.DefinitionAsync(ws, "Program.cs", 0, 10, "Program", CancellationToken.None);
            var count = r!["count"]!.GetValue<int>();
            var limit = r["limit"]!.GetValue<int>();
            var truncated = r["truncated"]!.GetValue<bool>();
            var symbol = r["symbol"]!.GetValue<string>();
            var locations = (JsonArray)r["locations"]!;
            var first = (JsonObject)locations[0]!;
            var startLine = first["start_line"]!.GetValue<int>();
            var endLine = first["end_line"]!.GetValue<int>();
            var ctx = (JsonArray)first["context"]!;
            var ctx0 = (JsonObject)ctx[0]!;
            var ctx2 = (JsonObject)ctx[2]!;
            var ctx0Line = ctx0["line"]!.GetValue<int>();
            var ctx0InSymbol = ctx0["in_symbol"]!.GetValue<bool>();
            var ctx2InSymbol = ctx2["in_symbol"]!.GetValue<bool>();
            var ctx0Text = ctx0["text"]!.GetValue<string>();
            Assert.Equal(5, count);
            Assert.Equal(5, limit);
            Assert.True(truncated);
            Assert.Equal("Program", symbol);
            Assert.Equal(5, locations.Count);
            Assert.Equal(1, startLine);
            Assert.Equal(1, endLine);
            Assert.Equal(3, ctx.Count);
            Assert.Equal(1, ctx0Line);
            Assert.True(ctx0InSymbol);
            Assert.False(ctx2InSymbol);
            Assert.Equal("using System;", ctx0Text);
        }
        finally { await ws.Ls.DisposeAsync(); }
    }

    [Fact]
    public async Task References_LimitsTo30_WithTruncationFlag()
    {
        var (qi, ws) = Attach("coder-06", MakeGitRepo("repo2"));
        try
        {
            var r = await qi.ReferencesAsync(ws, "Program.cs", 0, 10, "Program", CancellationToken.None);
            var count = r!["count"]!.GetValue<int>();
            var limit = r["limit"]!.GetValue<int>();
            var truncated = r["truncated"]!.GetValue<bool>();
            var locCount = ((JsonArray)r["locations"]!).Count;
            Assert.Equal(30, count);
            Assert.Equal(30, limit);
            Assert.True(truncated);
            Assert.Equal(30, locCount);
        }
        finally { await ws.Ls.DisposeAsync(); }
    }

    [Fact]
    public async Task Hover_FlattensFirstContentsEntry()
    {
        var (qi, ws) = Attach("coder-06", MakeGitRepo("repo3"));
        try
        {
            var r = await qi.HoverAsync(ws, "Program.cs", 1, 5, CancellationToken.None);
            var empty = r!["empty"]!.GetValue<bool>();
            var content = r["content"]!.GetValue<string>();
            var line = r["line"]!.GetValue<int>();
            Assert.False(empty);
            Assert.Contains("fake hover", content);
            Assert.Equal(1, line);
        }
        finally { await ws.Ls.DisposeAsync(); }
    }

    [Fact]
    public async Task Symbols_FlattensAndLimitsTo20()
    {
        var (qi, ws) = Attach("coder-06", MakeGitRepo("repo4"));
        try
        {
            var r = await qi.SymbolsAsync(ws, "Program.cs", CancellationToken.None);
            var count = r!["count"]!.GetValue<int>();
            var limit = r["limit"]!.GetValue<int>();
            var truncated = r["truncated"]!.GetValue<bool>();
            var arr = (JsonArray)r["symbols"]!;
            var first = (JsonObject)arr[0]!;
            var name = first["name"]!.GetValue<string>();
            var startLine = first["start_line"]!.GetValue<int>();
            Assert.Equal(20, count);
            Assert.Equal(20, limit);
            Assert.True(truncated);
            Assert.Equal(20, arr.Count);
            Assert.Equal("sym0", name);
            Assert.Equal(1, startLine);
        }
        finally { await ws.Ls.DisposeAsync(); }
    }

    [Fact]
    public async Task Diagnostics_ErrorsOnly_DeltaAndPerFileLimit()
    {
        var (qi, ws) = Attach("coder-06", MakeGitRepo("repo5"));
        try
        {
            var first = await qi.DiagnosticsAsync(ws, CancellationToken.None);
            var delta1 = first!["delta"]!.GetValue<int>();
            var files = (JsonArray)first["files"]!;
            var file = (JsonObject)files[0]!;
            var errors = (JsonArray)file["errors"]!;
            var fileTrunc = file["truncated"]!.GetValue<bool>();
            var fe = (JsonObject)errors[0]!;
            var feStart = fe["start_line"]!.GetValue<int>();
            var feEnd = fe["end_line"]!.GetValue<int>();
            var feSev = fe["severity"]!.GetValue<string>();
            Assert.Equal(20, delta1);
            Assert.Single(files);
            Assert.Equal(20, errors.Count);
            Assert.True(fileTrunc);
            Assert.Equal(1, feStart);
            Assert.Equal(5, feEnd);
            Assert.Equal("error", feSev);
            foreach (var e in errors!)
            {
                var code = e!["code"]!.GetValue<string>();
                Assert.DoesNotContain("CSW", code);
            }

            var second = await qi.DiagnosticsAsync(ws, CancellationToken.None);
            var delta2 = second!["delta"]!.GetValue<int>();
            var secondFiles = (JsonArray)second!["files"]!;
            Assert.Equal(0, delta2);
            Assert.Empty(secondFiles);

            qi.Forget(ws.Key);
            var third = await qi.DiagnosticsAsync(ws, CancellationToken.None);
            var delta3 = third!["delta"]!.GetValue<int>();
            Assert.Equal(20, delta3);
        }
        finally { await ws.Ls.DisposeAsync(); }
    }

    [Fact]
    public async Task SlotTable_FullTableYields429WithRetryAfter()
    {
        var opts = Options.Create(new CodeIntelOptions
        {
            WorkspaceRoot = Path.Combine(_root, "ws429"),
            LsKind = "fake",
            ReadyTimeout = TimeSpan.FromSeconds(15),
        });
        var svc = Options.Create(new CodeIntelServiceOptions
        {
            MaxLive = 3,
            IdleTtl = TimeSpan.FromMinutes(15),
            StartProcess = _ =>
            {
                var logFile = Path.Combine(_root, $"fake-429-{Guid.NewGuid():N}.log");
                File.Create(logFile).Dispose();
                var psi = new ProcessStartInfo("dotnet")
                {
                    ArgumentList = { _fakeDll, logFile },
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                var p = Process.Start(psi)!;
                _fakeProcesses.Add(p);
                return p;
            },
        });
        await using var wm = new WorkspaceManager(opts, svc, NullLogger<WorkspaceManager>.Instance);
        for (var i = 0; i < 3; i++)
        {
            var ws = await wm.AttachAsync($"seat{i}", MakeGitRepo($"repo429{i}"), "main", null, CancellationToken.None);
            var ready = ws.Ls.WaitReadyAsync(CancellationToken.None);
#pragma warning disable xUnit1031
            Assert.True(ready.GetAwaiter().GetResult());
#pragma warning restore xUnit1031
        }
        Assert.Equal(3, wm.LiveCount);
        CodeIntelException ex = await Assert.ThrowsAsync<CodeIntelException>(
            () => wm.AttachAsync("seat3", MakeGitRepo("repo429x"), "main", null, CancellationToken.None));
        Assert.Equal(429, ex.HttpStatus);
        Assert.True(ex.RetryAfterSeconds > 0);
    }
}

public class LspPeerChunkedWriteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci2-peer-{Guid.NewGuid():N}");
    private readonly List<Process> _processes = new();
    private string _dll;

    public LspPeerChunkedWriteTests()
    {
        Directory.CreateDirectory(_root);
        _dll = BuildChunkedLs();
    }

    public void Dispose()
    {
        foreach (var p in _processes)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); p.WaitForExit(5000); p.Dispose(); }
            catch { }
        }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string BuildChunkedLs()
    {
        var dir = Path.Combine(_root, $"peer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), Encoding.UTF8.GetString(ChunkedLsProgram.ChunkedLsSource));
        File.WriteAllText(Path.Combine(dir, "peer.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <Nullable>disable</Nullable>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n");
        var psi = new ProcessStartInfo("dotnet", $"build {dir} -c Release -o {Path.Combine(dir, "out")} -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(240_000)) { p.Kill(true); throw new InvalidOperationException("chunked-ls build timed out"); }
        if (p.ExitCode != 0) throw new InvalidOperationException("chunked-ls build failed: " + err);
        var dll = Path.Combine(dir, "out", "peer.dll");
        if (!File.Exists(dll)) throw new InvalidOperationException("chunked-ls DLL not produced: " + err);
        return dll;
    }

    [Fact]
    public async Task RequestAsync_SurvivesChunkedBodyWrites()
    {
        var psi = new ProcessStartInfo("dotnet", _dll)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var process = Process.Start(psi)!;
        _processes.Add(process);
        var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
        await using var peer = new LspPeer(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream,
            onRequest: null);
        var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
        try
        {
            var result = await peer.RequestAsync("initialize", JsonNode.Parse("{}"), ct);
            var text = result!.GetValue<string>();
            // fake LS returns "x" + 'y' * 4096 => total length 4097.
            // The point of the test: the body survived a 150 ms mid-body flush gap,
            // so the full 4097-byte result must arrive intact.
            Assert.StartsWith("x", text);
            Assert.Equal(1 + 4096, text.Length);
            Assert.Equal('y', text[text.Length - 1]);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            string stderrText = await stderrTask; 
            try { File.AppendAllText("/tmp/lsppeer-debug.log", $"[{DateTime.UtcNow:O}] fake-LS stderr: {stderrText}\n"); } catch { }
        }
    }
}
