@ModelType ErrorViewModel
@Code
    ViewData("Title") = "Error"
End Code

<h1>Error</h1>
<h2>An error occurred while processing your request.</h2>

@If Model.ShowRequestId Then
    <p><strong>Request ID:</strong> @Model.RequestId</p>
End If
