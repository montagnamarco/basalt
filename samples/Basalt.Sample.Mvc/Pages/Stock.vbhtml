@Page
<h1>Stock</h1>
@Await Component.InvokeAsync("Stock", New With {.count = 7})
