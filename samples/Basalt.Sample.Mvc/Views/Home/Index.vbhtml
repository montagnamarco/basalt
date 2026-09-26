@Inject Basalt.Sample.Mvc.Services.IGreeter Greeter
@ModelType Customer
<h1>Hello @Model.Name</h1>
<p>@Greeter.Greet()</p>
<p>temp: @TempData("note")</p>
<p>Orders: @Model.Orders</p>
@Await Html.PartialAsync("_Badge")
@Await Component.InvokeAsync("Stock", New With {.count = Model.Orders})
<a href="@Url.Action("Index", "Admin")">admin</a>
<a asp-controller="Admin" asp-action="Index">helper link</a>
<a asp-page="/Hello">helper page</a>
<form method="post">
    <label asp-for="Name">Nome</label>
    <input asp-for="Name" />
</form>
<button disabled="@(Model.Orders < 0)">buy</button>
<input value="@Model.Name" />
@If Model.Orders > 2 Then
    <p class="vip">VIP customer</p>
End If
@Section Footer
    <span>page footer</span>
End Section
