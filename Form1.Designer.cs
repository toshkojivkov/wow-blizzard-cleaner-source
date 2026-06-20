namespace WoWCleaner;

partial class Form1
{
    /// <summary>
    ///  Задължителна designer променлива.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Освобождава използваните ресурси.
    /// </summary>
    /// <param name="disposing">true, ако managed ресурсите трябва да бъдат освободени.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Задължителен метод за Designer поддръжка.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        Text = "Form1";
    }

    #endregion
}
