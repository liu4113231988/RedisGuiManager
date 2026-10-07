using System.Threading.Tasks;
using System.Windows.Forms;

namespace RedisGuiManager
{
    /// <summary>
    /// Key-type dispatch for the right-hand preview panel. Split out of FormMain.cs to keep the
    /// main form focused on connection and tree management.
    ///
    /// Each method creates the editor for the Redis type on demand, hands it the target node, then
    /// awaits the initial page load so the caller does not paint rows for a stale key.
    /// </summary>
    public partial class FormMain
    {
        private async Task StringKeySelect(RedisClient client, TreeNode key)
        {
            if (userControl == null || (userControl as StringValueControl) == null)
            {
                CreateRedisShowTagControl<StringValueControl>();
            }

            if (userControl is StringValueControl stringValueControl)
            {
                stringValueControl.MainForm = this;
                stringValueControl.TargetNode = key;
                await stringValueControl.SetNewKey(client, key.Text);
            }
        }

        private async Task ListKeySelect(RedisClient client, TreeNode key)
        {
            if (userControl == null || (userControl as ListValueControl) == null)
            {
                CreateRedisShowTagControl<ListValueControl>();
            }

            if (userControl is ListValueControl listValueControl)
            {
                listValueControl.MainForm = this;
                listValueControl.TargetNode = key;
                await listValueControl.SetNewKey(client, key.Text);
            }
        }

        private async Task HashKeySelect(RedisClient client, TreeNode key)
        {
            if (userControl == null || (userControl as HashValueControl) == null)
            {
                CreateRedisShowTagControl<HashValueControl>();
            }

            if (userControl is HashValueControl hashValueControl)
            {
                hashValueControl.MainForm = this;
                hashValueControl.TargetNode = key;
                await hashValueControl.SetNewKey(client, key.Text);
            }
        }

        private async Task SetKeySelect(RedisClient client, TreeNode key)
        {
            if (userControl == null || (userControl as SetValueControl) == null)
            {
                CreateRedisShowTagControl<SetValueControl>();
            }

            if (userControl is SetValueControl setValueControl)
            {
                setValueControl.MainForm = this;
                setValueControl.TargetNode = key;
                await setValueControl.SetNewKey(client, key.Text);
            }
        }

        private async Task ZSetKeySelect(RedisClient client, TreeNode key)
        {
            if (userControl == null || (userControl as ZSetValueControl) == null)
            {
                CreateRedisShowTagControl<ZSetValueControl>();
            }

            if (userControl is ZSetValueControl zsetValueControl)
            {
                zsetValueControl.MainForm = this;
                zsetValueControl.TargetNode = key;
                await zsetValueControl.SetNewKey(client, key.Text);
            }
        }

        private async Task StreamKeySelect(RedisClient client, TreeNode key)
        {
            if (userControl == null || (userControl as StreamValueControl) == null)
            {
                CreateRedisShowTagControl<StreamValueControl>();
            }

            if (userControl is StreamValueControl streamValueControl)
            {
                streamValueControl.MainForm = this;
                streamValueControl.TargetNode = key;
                await streamValueControl.SetNewKey(client, key.Text);
            }
        }
    }
}