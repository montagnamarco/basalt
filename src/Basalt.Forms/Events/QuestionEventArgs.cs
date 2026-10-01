namespace System.Windows.Forms;

public class QuestionEventArgs : EventArgs
{
    public QuestionEventArgs()
    {
    }

    public QuestionEventArgs(bool response)
    {
        Response = response;
    }

    public bool Response { get; set; }
}
