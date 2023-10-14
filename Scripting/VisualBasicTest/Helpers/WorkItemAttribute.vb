
<AttributeUsage(AttributeTargets.All)>
Public Class WorkItemAttribute
    Inherits Attribute

    Sub New(link As String)

    End Sub

    Sub New(id As Integer, name As String)

    End Sub
End Class
