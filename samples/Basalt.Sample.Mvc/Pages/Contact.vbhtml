@Page
@ModelType Basalt.Sample.Mvc.Pages.ContactModel
<p>@Model.Message</p>
<form method="post">
    @Html.AntiForgeryToken()
    <input name="Name" value="Ada" />
    <button type="submit">send</button>
</form>
