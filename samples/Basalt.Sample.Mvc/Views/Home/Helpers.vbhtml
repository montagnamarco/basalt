@ModelType Basalt.Sample.Mvc.Models.Order
<form asp-action="Helpers" method="post">
    <label asp-for="Customer"></label>
    <input asp-for="Customer" />
    <span asp-validation-for="Customer"></span>
    <input asp-for="Quantity" />
    <select asp-for="Colour" asp-items="Model.Colours"></select>
    <textarea asp-for="Customer"></textarea>
</form>
<a asp-action="Helpers" asp-route-id="42">route</a>
<partial name="_Badge" />
<environment include="Development"><p>in development</p></environment>
<environment exclude="Development"><p>not development</p></environment>
<shout text="quiet please"></shout>
<shout text="level" level-id="3"></shout>
<input asp-for="Colour" id="off" disabled="@(Model.Quantity > 5)" />
<input asp-for="Colour" id="on" readonly="@(Model.Quantity < 5)" />
<partial name="_Badge">
<!-- <shout text="commented"></shout> -->
