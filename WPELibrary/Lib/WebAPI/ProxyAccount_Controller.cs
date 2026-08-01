using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Http;

namespace WPELibrary.Lib.WebAPI
{
    [RoutePrefix("ProxyAccount")]

    public class ProxyAccount_Controller : ApiController
    {
        #region//获取代理账号列表

        [HttpGet]
        [Route("GetProxyAccountList")]

        public IEnumerable<ProxyAccountSummary> GetProxyAccountList()
        {
            return Socket_Cache.ProxyAccount.lstProxyAccount.Select(ProxyAccountSummary.FromAccount);
        }

        #endregion

        #region//获取代理账号

        [HttpGet]
        [Route("GetProxyAccountByID")]

        public ProxyAccountSummary GetProxyAccountByID(Guid AID)
        {
            Proxy_AccountInfo account = Socket_Cache.ProxyAccount.GetProxyAccount_ByAccountID(AID);
            return account == null ? null : ProxyAccountSummary.FromAccount(account);
        }

        #endregion

        #region//新增代理账号

        [HttpPost]
        [Route("AddProxyAccount")]

        public IHttpActionResult AddProxyAccount([FromBody] Proxy_AccountInfo pai)
        {
            try
            {
                if (Socket_Cache.ProxyAccount.CheckProxyAccount_Exist(pai.UserName))
                {
                    return BadRequest(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_177));
                }

                pai.LoginTime = DateTime.MinValue;

                if (pai.ExpiryTime == null)
                {
                    pai.ExpiryTime = DateTime.Now;
                }

                pai.PassWord = Socket_Operation.PassWord_Encrypt(pai.PassWord);
                bool bOK = Socket_Cache.ProxyAccount.AddProxyAccount(
                    Guid.NewGuid(), 
                    pai.IsEnable, 
                    pai.UserName, 
                    pai.PassWord, 
                    pai.LoginTime, 
                    string.Empty, 
                    string.Empty, 
                    pai.IsLimitLinks,
                    pai.LimitLinks,
                    pai.IsLimitDevices,
                    pai.LimitDevices,
                    pai.IsExpiry, 
                    pai.ExpiryTime, 
                    DateTime.Now);

                if (bOK)
                {
                    return Ok(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_183));
                }
                else
                {
                    return BadRequest(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_181));
                }
            }
            catch (Exception ex)
            {
                Socket_Operation.DoLog_Proxy(MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            return BadRequest(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_181));
        }

        #endregion

        #region//删除代理账号

        [HttpPost]
        [Route("DeleteProxyAccount")]

        public IHttpActionResult DeleteProxyAccount([FromBody] Guid AID)
        {
            bool bOK = Socket_Cache.ProxyAccount.DeleteProxyAccount_ByAccountID(AID);

            if (bOK)
            {
                return Ok(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_184));
            }
            else
            {
                return BadRequest(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_182));
            }
        }

        #endregion

        #region//更新代理账号

        [HttpPost]
        [Route("UpdateProxyAccount")]

        public IHttpActionResult UpdateProxyAccount([FromBody] Proxy_AccountInfo pai)
        {
            if (pai.ExpiryTime == null)
            {
                pai.ExpiryTime = DateTime.Now;
            }

            if (!string.IsNullOrEmpty(pai.PassWord))
            {
                pai.PassWord = Socket_Operation.PassWord_Encrypt(pai.PassWord);
            }
            else
            {
                Proxy_AccountInfo existing = Socket_Cache.ProxyAccount.GetProxyAccount_ByAccountID(pai.AID);
                if (existing == null)
                {
                    return BadRequest(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_195));
                }

                pai.PassWord = existing.PassWord;
            }

            bool bOK = Socket_Cache.ProxyAccount.UpdateProxyAccount_ByAccountID(
                pai.AID, 
                pai.IsEnable, 
                pai.PassWord, 
                pai.IsLimitLinks,
                pai.LimitLinks,
                pai.IsLimitDevices,
                pai.LimitDevices,
                pai.IsExpiry, 
                pai.ExpiryTime);

            if (bOK)
            {
                return Ok(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_194));
            }
            else
            {
                return BadRequest(MultiLanguage.GetDefaultLanguage(MultiLanguage.MutiLan_195));
            }            
        }

        #endregion

        public sealed class ProxyAccountSummary
        {
            public Guid AID { get; set; }
            public bool IsEnable { get; set; }
            public string UserName { get; set; }
            public DateTime LoginTime { get; set; }
            public string LoginIP { get; set; }
            public string IPLocation { get; set; }
            public bool IsLimitLinks { get; set; }
            public int LimitLinks { get; set; }
            public bool IsLimitDevices { get; set; }
            public int LimitDevices { get; set; }
            public bool IsExpiry { get; set; }
            public DateTime ExpiryTime { get; set; }
            public DateTime CreateTime { get; set; }
            public bool IsOnLine { get; set; }

            public static ProxyAccountSummary FromAccount(Proxy_AccountInfo account)
            {
                return new ProxyAccountSummary
                {
                    AID = account.AID,
                    IsEnable = account.IsEnable,
                    UserName = account.UserName,
                    LoginTime = account.LoginTime,
                    LoginIP = account.LoginIP,
                    IPLocation = account.IPLocation,
                    IsLimitLinks = account.IsLimitLinks,
                    LimitLinks = account.LimitLinks,
                    IsLimitDevices = account.IsLimitDevices,
                    LimitDevices = account.LimitDevices,
                    IsExpiry = account.IsExpiry,
                    ExpiryTime = account.ExpiryTime,
                    CreateTime = account.CreateTime,
                    IsOnLine = account.IsOnLine
                };
            }
        }
    }
}
