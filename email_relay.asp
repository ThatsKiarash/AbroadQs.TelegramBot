<%@ Language="VBScript" %>
<%
Response.ContentType = "application/json"

Const SECRET_TOKEN = "AbroadQs_Email_Relay_2026_Secure"
Const FROM_EMAIL = "info@abroadqs.com"
Const FROM_NAME = "AbroadQs"

If Request.ServerVariables("REQUEST_METHOD") <> "POST" Then
    Response.Status = "405 Method Not Allowed"
    Response.Write "{""ok"":false,""error"":""Method not allowed""}"
    Response.End
End If

' Read the raw POST body
Dim bodyStr
If Request.TotalBytes > 0 Then
    Dim stream
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 1
    stream.Open
    stream.Write Request.BinaryRead(Request.TotalBytes)
    stream.Position = 0
    stream.Type = 2
    stream.Charset = "utf-8"
    bodyStr = stream.ReadText
    stream.Close
    Set stream = Nothing
Else
    Response.Status = "400 Bad Request"
    Response.Write "{""ok"":false,""error"":""Empty body""}"
    Response.End
End If

' Simple JSON value extraction
Function GetJsonVal(json, key)
    Dim re
    Set re = New RegExp
    re.Pattern = """" & key & """\s*:\s*""([^""]*)"""
    re.IgnoreCase = True
    If re.Test(json) Then
        GetJsonVal = re.Execute(json)(0).SubMatches(0)
    Else
        GetJsonVal = ""
    End If
End Function

Dim token, toAddr, subject, htmlBody
token = GetJsonVal(bodyStr, "token")
toAddr = GetJsonVal(bodyStr, "to")
subject = GetJsonVal(bodyStr, "subject")
htmlBody = GetJsonVal(bodyStr, "body")

If token <> SECRET_TOKEN Then
    Response.Status = "403 Forbidden"
    Response.Write "{""ok"":false,""error"":""Unauthorized""}"
    Response.End
End If

If toAddr = "" Or subject = "" Or htmlBody = "" Then
    Response.Status = "400 Bad Request"
    Response.Write "{""ok"":false,""error"":""Missing to, subject, or body""}"
    Response.End
End If

' Send email using CDO.Message
Dim msg
Set msg = CreateObject("CDO.Message")
msg.From = FROM_NAME & " <" & FROM_EMAIL & ">"
msg.To = toAddr
msg.Subject = subject
msg.HTMLBody = htmlBody

' Configure SMTP - try local pickup first, then localhost SMTP
msg.Configuration.Fields.Item("http://schemas.microsoft.com/cdo/configuration/sendusing") = 2
msg.Configuration.Fields.Item("http://schemas.microsoft.com/cdo/configuration/smtpserver") = "localhost"
msg.Configuration.Fields.Item("http://schemas.microsoft.com/cdo/configuration/smtpserverport") = 25
msg.Configuration.Fields.Update

On Error Resume Next
msg.Send

If Err.Number <> 0 Then
    ' Try pickup directory method
    Err.Clear
    msg.Configuration.Fields.Item("http://schemas.microsoft.com/cdo/configuration/sendusing") = 1
    msg.Configuration.Fields.Update
    msg.Send
    
    If Err.Number <> 0 Then
        Response.Status = "500 Internal Server Error"
        Response.Write "{""ok"":false,""error"":""" & Replace(Err.Description, """", "'") & """}"
        Set msg = Nothing
        Response.End
    End If
End If

On Error GoTo 0
Set msg = Nothing

Response.Write "{""ok"":true}"
%>
