using System;
using System.Drawing;
using System.Windows.Forms;
using EveOPreview.Configuration;
using EveOPreview.Services;

namespace EveOPreview.View
{
	public partial class ThumbnailOverlay : Form
	{
		#region Private fields
		private readonly Action<object, MouseEventArgs> _areaClickAction;
		#endregion

		public ThumbnailOverlay(Form owner, Action<object, MouseEventArgs> areaClickAction)
		{
			this.Owner = owner;
			this._areaClickAction = areaClickAction;

			InitializeComponent();
		}

		private void OverlayArea_Click(object sender, MouseEventArgs e)
		{
			this._areaClickAction(this, e);
		}

		public void SetOverlayLabel(string label)
		{
			this.OverlayLabel.Text = label;
		}

		public void SetPropertiesOverlayLabel(Font f, System.Drawing.Color c, ZoomAnchor anchor)
		{
			if (
				this.OverlayLabel.Font.Size != f.Size ||
				this.OverlayLabel.Font.FontFamily != f.FontFamily ||
				this.OverlayLabel.Font.Italic != f.Italic ||
				this.OverlayLabel.Font.Bold != f.Bold
				)
			{
				this.OverlayLabel.Font = f;
			}
			this.OverlayLabel.ForeColor = c;

			int margin = 5;

			switch (anchor)
			{
				case ZoomAnchor.NW:
					this.OverlayLabel.Left = margin;
					this.OverlayLabel.Top = margin;
					this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.TopLeft;
					break;
                case ZoomAnchor.N:
                    this.OverlayLabel.Left = (this.Width / 2) - (this.OverlayLabel.Width / 2);
                    this.OverlayLabel.Top = margin;
                    this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.TopCenter;
                    break;
                case ZoomAnchor.NE:
                    this.OverlayLabel.Left = this.Width - this.OverlayLabel.Width - margin;
                    this.OverlayLabel.Top = margin;
                    this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.TopRight;
                    break;
                case ZoomAnchor.W:
                    this.OverlayLabel.Left = margin;
                    this.OverlayLabel.Top = (this.Height / 2) - (this.OverlayLabel.Height / 2);
                    this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                    break;
                case ZoomAnchor.C:
                    this.OverlayLabel.Left = (this.Width / 2) - (this.OverlayLabel.Width / 2);
                    this.OverlayLabel.Top = (this.Height / 2) - (this.OverlayLabel.Height / 2);
                    this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
                    break;
                case ZoomAnchor.E:
                    this.OverlayLabel.Left = this.Width - this.OverlayLabel.Width - margin;
                    this.OverlayLabel.Top = (this.Height / 2) - (this.OverlayLabel.Height / 2);
                    this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
                    break;
                case ZoomAnchor.SW:
                    this.OverlayLabel.Left = margin;
                    this.OverlayLabel.Top = this.Height - this.OverlayLabel.Height - margin;
                    this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
                    break;
                case ZoomAnchor.S:
                    this.OverlayLabel.Left = (this.Width / 2) - (this.OverlayLabel.Width / 2);
                    this.OverlayLabel.Top = this.Height - this.OverlayLabel.Height - margin;
                    this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
                    break;
                case ZoomAnchor.SE:
                    this.OverlayLabel.Left = this.Width - this.OverlayLabel.Width - margin;
                    this.OverlayLabel.Top = this.Height - this.OverlayLabel.Height - margin;
                    this.OverlayLabel.TextAlign = System.Drawing.ContentAlignment.BottomRight;
                    break;
            }
		}

		public void EnableOverlayLabel(bool enable)
		{
			this.OverlayLabel.Visible = enable;
		}

		protected override CreateParams CreateParams
		{
			get
			{
				var Params = base.CreateParams;
				Params.ExStyle |= (int)InteropConstants.WS_EX_TOOLWINDOW;
				return Params;
			}
		}
	}
}
