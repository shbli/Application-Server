using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using MongoDB.Driver.GridFS;
using MongoDB.Driver.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Mail;
using System.Web.Script.Services;


/// <summary>
/// Summary description for WebService
/// </summary>
[WebService(Namespace = "http://oraiariftserver.azurewebsites.net/")]
[WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
// To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
// [System.Web.Script.Services.ScriptService]
public class WebService : System.Web.Services.WebService
{
    MongoClient client;
    MongoServer server;
    MongoDatabase database;
    public const string connectionString = "mongodb://OraiaRiftMongo:d_FnoSXoSWBV0KffjS9NLvoahTvDO_FkbIifr.8JALM-@ds050087.mongolab.com:50087/OraiaRiftMongo";
    public const string dataBaseName = "OraiaRiftMongo";
    const string facebookAppID = "323832914468001";
    const string twittwrConsumerKey = "pxgprmUwzYSUUZoayIBnpQyBj";
    const string twitterConsumerSecret = "9dsSm1blwVLHhnBFIvuxvkgiArJyWELdzpGgBYAKtVdOyH4deM";
    const string FullmemberType = "FullMember";
    const string FacebookUserType = "Facebook user";
    const string TwitterUserType = "Twitter user";
    const string GuestUserType = "Guest";

    public WebService()
    {
        //Initlize the connection to the MongoDB database
        client = new MongoClient(connectionString);
        server = client.GetServer();
        database = server.GetDatabase(dataBaseName);
    }


    [WebMethod]
    [ScriptMethod(UseHttpGet = true)]
    public string DateTimeFromServer()
    {
        //this is used by the client to determine the time on server, it is used to keep day/night time consistent between player dueling againest each other
        BsonDocument returnDoc = new BsonDocument { { "time", DateTime.Now.ToString() } };
        return returnDoc.ToString();
    }

    //Recieves an Apple recipt and validate it, if valid subscribe the player, this API is not used anymore in the game
    [WebMethod]
    public string Subscribe(string token, string recipt)
    {
        if (token.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token too short" } };
            return returnDoc.ToString();
        }

        if (recipt.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "recipt too short" } };
            return returnDoc.ToString();
        }

        string userID = ValidateToken(token);
        if (userID == "")
        {
            SubscriptionHistory("Subscription failed, invalid token", "UNABLE TO RETRIVE userID", token, "");

            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "invalid token" } };
            return returnDoc.ToString();

        }
        else
        {
            BsonDocument apple_response = VerifyAppleRecipt(userID, recipt);
            if (apple_response["status"].ToBoolean() == false)
            {
                SubscriptionHistory("Invalid Recipt", userID, token, "");            //Product purchasing history------------

                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Invalid Recipt" } };
                return returnDoc.ToString();
            }
            string productID = apple_response["p_id"].ToString();
            string transaction_id = apple_response["t_id"].ToString();
            MongoCollection UserSubscriptiosCollection = database.GetCollection("UserSubscriptions");
            if (ExistInCollection(UserSubscriptiosCollection, "TransactionID", transaction_id))
            {
                SubscriptionHistory("Subscription failed, Transaction ID already exists", userID, token, productID);            //Product purchasing history------------

                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Transaction ID already exists" } };
                return returnDoc.ToString();
            }
            int days = ValidateSubscriptionId(productID);
            if (days == -1)
            {
                SubscriptionHistory("Subscription failed, invalid productID", userID, token, productID);            //subscription history------------

                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "invalid ProductID" } };
                return returnDoc.ToString();
            }
            else
            {
                // the product ID is valid, let's continue
                MongoCollection usersCollection = database.GetCollection("users");

                IMongoQuery query = Query.EQ("token", token);
                FieldsBuilder fields = Fields.Include("_id").Include("subscription_end_date");
                BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();

                string dateAndTimeString = result["subscription_end_date"].ToString();

                //this is a valid string
                DateTime subscriptionEndDate = DateTime.Parse(dateAndTimeString);
                DateTime today = DateTime.Today;

                if (DateTime.Compare(today, subscriptionEndDate) > 0)
                {
                    //if subscription is already exprired, we will calculate the subscription starting from today date
                    DateTime newdate = today.AddDays(days);
                    //updating the userID with the new date
                    IMongoQuery query3 = Query.EQ("userID", userID);
                    IMongoUpdate update2 = Update.Set("subscription_end_date", newdate);
                    usersCollection.Update(query3, update2);
                    BsonDocument usersubscription = new BsonDocument(){{"userID",userID},{"token",token},{"ProductID",productID},{"TransactionID",transaction_id},
                    {"PreviousSubscriptionDate",subscriptionEndDate},{"NewSubscriptionEndDate",newdate},{"subscription date",today}};
                    UserSubscriptiosCollection.Insert(usersubscription);
                    SubscriptionHistory("Successfully subscribed, previous date - " + subscriptionEndDate.ToString() + " , new date - " + newdate.ToString(),
                        userID, token, productID);

                    BsonDocument returnDoc = new BsonDocument { { "status", false }, { "subscription_end_date", newdate } };
                    return returnDoc.ToString();
                }
                else                                              //if subscription is valid
                {
                    DateTime newdate = subscriptionEndDate.AddDays(days);
                    IMongoQuery query3 = Query.EQ("userID", userID);
                    IMongoUpdate update2 = Update.Set("subscription_end_date", newdate);
                    usersCollection.Update(query3, update2);
                    BsonDocument usersubscription = new BsonDocument(){{"userID",userID},{"token",token},{"ProductID",productID},{"TransactionID",transaction_id},
                    {"PreviousSubscriptionDate",subscriptionEndDate},{"NewSubscriptionEndDate",newdate},{"subscription date",today}};
                    UserSubscriptiosCollection.Insert(usersubscription);

                    SubscriptionHistory(
                        "Successfully subscribed, previous date - " + subscriptionEndDate.ToString() + " , new date - " + newdate.ToString(),
                        userID, token, productID);

                    BsonDocument returnDoc = new BsonDocument { { "status", true }, { "subscription_end_date", newdate } };
                    return returnDoc.ToString();

                }
            }

        }
    }



    [WebMethod]
    public string Register(string username, string password)
    {
        if (IsValidEmail(username) == false)
        {
            BsonDocument returnDoc2 = new BsonDocument { { "status", false }, { "error", "Enter a valid e-mail ID" } };
            return returnDoc2.ToString();
        }

        if (password.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "password too short" } };
            return returnDoc.ToString();
        }

        DateTime today = DateTime.Today;
        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.And(Query.EQ("username", username), Query.EQ("TypeOfUser", FullmemberType));
        FieldsBuilder fields = Fields.Include("_id");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {

            DateTime trialpackage = today.AddDays(30);
            DateTime now = DateTime.Now;
            string token = GetUniqueKey();
            string verification_token = GetUniqueKey();
            BsonDocument document = new BsonDocument { { "username", username }, { "password", EncodePasswordToBase64(password) }, { "TypeOfUser", FullmemberType }, { "token", token },
            { "subscription_end_date", trialpackage } ,{"registration_time",now.ToString()},{"last_login",today.ToString()},{"verification_token", verification_token},{"verified",false}};
            usersCollection.Insert(document);
            RegistrationHistory("Successfully Registered", username, FullmemberType, token);
            SendEmail("Successfully Registered", "Dear " + username + " ,Please click on the following link to confirm your registration- " + "http://oraiariftserver.azurewebsites.net/verify_e-mail.aspx?verification_token=" + verification_token, username);
            BsonDocument returnDoc = new BsonDocument { { "status", true }, { "token", token } };
            return returnDoc.ToString();

        }
        else
        {
            RegistrationHistory("username already exists", "tried using-" + username, FullmemberType, "token not issued");
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "user name already exist" } };
            return returnDoc.ToString();
        }

    }


    [WebMethod]
    public string FullMemberLogin(string username, string password)
    {
        if (username.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username too short" } };
            return returnDoc.ToString();
        }

        if (password.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "password too short" } };
            return returnDoc.ToString();
        }
        string encryptedPassword = EncodePasswordToBase64(password);
        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.And(Query.EQ("username", username), Query.EQ("TypeOfUser", FullmemberType));
        FieldsBuilder fields = Fields.Include("_id").Include("password");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        string today = DateTime.Today.ToString();
        if (result == null)
        {
            LoginHistory("username does not exist", "Tried using-" + username, "user does not exist", "not issued");

            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username does not exist" } };
            return returnDoc.ToString();
        }

        else
        {

            if (encryptedPassword == result["password"].ToString())
            {
                string token = GetUniqueKey();
                IMongoUpdate update = Update.Set("token", token).Set("last_login", today.ToString());

                usersCollection.Update(query, update);

                LoginHistory("Successfully logged in", username, FullmemberType, token);

                BsonDocument returnDoc = new BsonDocument { { "status", true }, { "token", token } };

                return returnDoc.ToString();

            }

            else
            {
                LoginHistory("authentication failed", username, FullmemberType, "not issued");
                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "authentication failed" } };
                return returnDoc.ToString();
            }
        }
    }



    [WebMethod]
    public string FacebookUserLogin(string facebookUserID, string facebookToken)
    {

        if (facebookUserID.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "facebook_Username too short" } };
            return returnDoc.ToString();
        }

        if (facebookToken.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "facebookToken too short" } };
            return returnDoc.ToString();
        }

        BsonDocument facebookResult = ValidateFacebookUser(facebookUserID, facebookToken);
        if (facebookResult["status"].ToBoolean() == false)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "facebook user not verified" } };
            return returnDoc.ToString();
        }

        BsonDocument facebookResult2 = ValidateFacebookAppID(facebookToken);
        if (facebookResult2["status"].ToBoolean() == false)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "facebook AppID not verified" } };
            return returnDoc.ToString();
        }

        //facebook user verified
        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.And(Query.EQ("username", facebookUserID), Query.EQ("TypeOfUser", FacebookUserType));
        FieldsBuilder fields = Fields.Include("_id").Include("TypeOfUser");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        DateTime today = DateTime.Today;
        if (result == null)
        {
            DateTime trialpackage = DateTime.Today.AddDays(30);
            string token = GetUniqueKey();
            BsonDocument document = new BsonDocument { { "username", facebookUserID }, { "TypeOfUser", FacebookUserType }, { "token", token },
                { "subscription_end_date", trialpackage }, { "registration_time", today.ToString() }, { "last_login", today.ToString() },{"verified",false} };
            usersCollection.Insert(document);
            RegistrationHistory("Successfully Registered", facebookUserID, FacebookUserType, token);
            result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
            BsonDocument returnDoc = new BsonDocument { { "status", true }, { "token", token } };
            return returnDoc.ToString();
        }
        else
        {
            string token = GetUniqueKey();
            IMongoUpdate update = Update.Set("token", token).Set("last_login", today.ToString());
            usersCollection.Update(query, update);
            LoginHistory("Successfully logged in", facebookUserID, FacebookUserType, token);
            BsonDocument returnDoc = new BsonDocument { { "status", true }, { "token", token } };
            return returnDoc.ToString();
        }

    }

    [WebMethod]
    public string TwitterUserLogin(string TwitterUsername, string accessToken, string accessSecret)
    {

        if (TwitterUsername.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "TwitterUsername too short" } };
            return returnDoc.ToString();
        }

        if (accessToken.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "accessToken too short" } };
            return returnDoc.ToString();
        }

        if (accessSecret.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "accessSecret too short" } };
            return returnDoc.ToString();
        }

        BsonDocument twitterResult = VerifyTwitterCredentials(twittwrConsumerKey, twitterConsumerSecret, accessToken, accessSecret);
        if (twitterResult["status"].ToBoolean() == true)
        {     //twiiter user verified
            DateTime today = DateTime.Today;
            MongoCollection usersCollection = database.GetCollection("users");
            IMongoQuery query = Query.And(Query.EQ("username", TwitterUsername), Query.EQ("TypeOfUser", TwitterUserType));
            FieldsBuilder fields = Fields.Include("_id").Include("TypeOfUser");
            BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
            if (result == null)
            {
                DateTime trialpackage = DateTime.Today.AddDays(30);

                string token = GetUniqueKey();
                BsonDocument document = new BsonDocument { { "username", TwitterUsername }, { "TypeOfUser", TwitterUserType }, { "token", token },
                { "subscription_end_date", trialpackage },{ "registration_time", today.ToString() }, { "last_login", today.ToString() },{"verified",false} };
                usersCollection.Insert(document);
                RegistrationHistory("Successfully Registered", TwitterUsername, TwitterUserType, token);
                result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
                BsonDocument returnDoc = new BsonDocument { { "status", true }, { "token", token } };
                return returnDoc.ToString();
            }
            else
            {
                string token = GetUniqueKey();
                IMongoUpdate update = Update.Set("token", token).Set("last_login", today.ToString());
                usersCollection.Update(query, update);
                LoginHistory("Successfully logged in", TwitterUsername, TwitterUserType, token);
                BsonDocument returnDoc = new BsonDocument { { "status", true }, { "token", token } };
                return returnDoc.ToString();

            }

        }
        else
        {
            //twitter user not verfied
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "twitter user not verified" } };
            return returnDoc.ToString();

        }
    }




    [WebMethod]
    public string GuestLogin(string username, string password)
    {
        if (username.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username too short" } };
            return returnDoc.ToString();
        }

        if (password.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "password too short" } };
            return returnDoc.ToString();
        }
        string encryptedPassword = EncodePasswordToBase64(password);
        DateTime today = DateTime.Today;
        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.And(Query.EQ("username", username), Query.EQ("TypeOfUser", GuestUserType));
        FieldsBuilder fields = Fields.Include("_id").Include("password");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            DateTime trialpackage = DateTime.Today.AddDays(30);
            string token = GetUniqueKey();
            BsonDocument document = new BsonDocument { { "username", username }, { "password", EncodePasswordToBase64(password) },
            { "TypeOfUser", GuestUserType }, { "token", token }, { "subscription_end_date", trialpackage },
            { "registration_time", today.ToString() }, { "last_login", today.ToString() },{"verified",false} };
            usersCollection.Insert(document);
            RegistrationHistory("Successfully Registered", username, GuestUserType, token);
            BsonDocument returnDoc = new BsonDocument { { "status", true }, { "token", token } };
            return returnDoc.ToString();

        }

        if (encryptedPassword == result["password"].ToString())
        {
            string token = GetUniqueKey();
            IMongoUpdate update = Update.Set("token", token).Set("last_login", today.ToString());
            usersCollection.Update(query, update);
            LoginHistory("Successfully logged in", username, GuestUserType, token);                                //login history-----
            BsonDocument returnDoc = new BsonDocument { { "status", true }, { "token", token } };
            return returnDoc.ToString();
        }

        else
        {
            LoginHistory("authentication failed", username, GuestUserType, "not issued");
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "authentication failed" } };
            return returnDoc.ToString();
        }

    }


    //a method to buy an item, recieves apple recipts
    [WebMethod]
    public string BuyItem(string token, string recipt)
    {

        if (token.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token too short" } };
            return returnDoc.ToString();
        }

        if (recipt.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "recipt too short" } };
            return returnDoc.ToString();
        }

        string userID = ValidateToken(token);
        if (userID == "")
        {
            UserProductHistory("Purchasing failed, invalid token", "UNABLE TO RETRIVE USERNAME", token, "");        //Product purchasing history------

            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "invalid token" } };
            return returnDoc.ToString();
        }

        else
        {
            BsonDocument apple_response = VerifyAppleRecipt(userID, recipt);
            if (apple_response["status"].ToBoolean() == false)
            {
                UserProductHistory("Invalid Recipt", userID, token, "");            //Product purchasing history------------

                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Invalid Recipt" } };
                return returnDoc.ToString();
            }
            string productid = apple_response["p_id"].ToString();
            string transaction_id = apple_response["t_id"].ToString();
            BsonDocument ProductDetail = ValidateProduct(productid);
            bool productStatus = ProductDetail["status"].ToBoolean();
            if (productStatus == false)
            {
                UserProductHistory("Purchasing failed, invalid productID", userID, token, productid);           //Product purchasing history------------
                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Invalid ProductID" } };
                return returnDoc.ToString();
            }
            bool IsGift = ProductDetail["IsGift"].ToBoolean();
            if (IsGift == true)
            {
                UserProductHistory("Purchasing failed, Product is a Gift", userID, token, productid);           //Product purchasing history------------
                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Product is a Gift" } };
                return returnDoc.ToString();
            }
            MongoCollection BuyItemsCollection = database.GetCollection("UserProducts");
            if (ExistInCollection(BuyItemsCollection, "transaction_id", transaction_id))
            {
                UserProductHistory("Purchasing failed, Transaction ID already exists", userID, token, productid);            //Product purchasing history------------
                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Transaction ID already exists" } };
                return returnDoc.ToString();
            }

            BsonDocument document = new BsonDocument { { "userID", userID }, { "ProductID", productid }, { "ProductName", ProductDetail["Name"].ToString() }, { "transaction_id", transaction_id } };
            BuyItemsCollection.Insert(document);
            UserProductHistory("Purchase successful", userID, token, productid);
            BsonDocument returnDoc2 = new BsonDocument { { "status", true }, { "Product", ProductDetail["Name"].ToString() } };
            return returnDoc2.ToString();
        }
    }



    //Used to contact Apple server to validate the recipt, the parameter url is changeable since it is a requirment to check againest both live and sandbox, check status 21007 for the sandbox case, it uses a one time recrusion, 21007 case only happen with internal testing
    private BsonDocument VerifyAppleRecipt(string userName, string recipt, string url = "https://buy.itunes.apple.com/verifyReceipt")
    {
        BsonDocument reciptDocument = new BsonDocument() { { "receipt-data", /*The recipt is base 64 encoded by the client already*/recipt.ToString() } };
        //the token number is valid
        byte[] postDataBytes = System.Text.Encoding.UTF8.GetBytes(reciptDocument.ToString());
        string appleResponse = PostRequest(url, postDataBytes);
        BsonDocument appleResponseDocument = BsonDocument.Parse(appleResponse);
        int status = appleResponseDocument["status"].ToInt32();
        if (status == 21000)
        {
            ReciptHistoryRecord("21000", userName, recipt.ToString(), "The App Store could not read the JSON object you provided", appleResponse);
            //The App Store could not read the JSON object you provided.
            BsonDocument document = new BsonDocument() { { "status", false } };
            return document;
        }

        if (status == 21002)
        {
            ReciptHistoryRecord("21002", userName, recipt.ToString(), "The data in the receipt-data property was malformed or missing.", appleResponse);
            //The data in the receipt-data property was malformed or missing.
            BsonDocument document = new BsonDocument() { { "status", false } };
            return document;
        }

        if (status == 21003)
        {
            ReciptHistoryRecord("21003", userName, recipt.ToString(), "The receipt could not be authenticated.", appleResponse);
            //The receipt could not be authenticated.
            BsonDocument document = new BsonDocument() { { "status", false } };
            return document;
        }

        if (status == 21004)
        {
            ReciptHistoryRecord("21004", userName, recipt.ToString(), "The shared secret you provided does not match the shared secret on file for your account.Only returned for iOS 6 style transaction receipts for auto-renewable subscriptions.", appleResponse);
            //The shared secret you provided does not match the shared secret on file for your account.
            //Only returned for iOS 6 style transaction receipts for auto-renewable subscriptions.
            BsonDocument document = new BsonDocument() { { "status", false } };
            return document;
        }

        if (status == 21005)
        {
            ReciptHistoryRecord("21005", userName, recipt.ToString(), "The receipt server is not currently available.", appleResponse);
            //The receipt server is not currently available.
            BsonDocument document = new BsonDocument() { { "status", false } };
            return document;
        }


        if (status == 21006)
        {
            ReciptHistoryRecord("21006", userName, recipt.ToString(), "This receipt is valid but the subscription has expired. When this status code is returned to your server, the receipt data is also decoded and returned as part of the response.Only returned for iOS 6 style transaction receipts for auto-renewable subscriptions.", appleResponse);
            //This receipt is valid but the subscription has expired. When this status code is returned to your server, the receipt data is also decoded and returned as part of the response.
            //Only returned for iOS 6 style transaction receipts for auto-renewable subscriptions.
            BsonDocument document = new BsonDocument() { { "status", false } };
            return document;
        }

        if (status == 21007)
        {
            ReciptHistoryRecord("21007", userName, recipt.ToString(), "This receipt is from the test environment, but it was sent to the production environment for verification. Send it to the test environment instead.", appleResponse);
            //This receipt is from the test environment, but it was sent to the production environment for verification. Send it to the test environment instead.
            return VerifyAppleRecipt(userName, recipt, "https://sandbox.itunes.apple.com/verifyReceipt");
        }
        if (status == 21008)
        {
            ReciptHistoryRecord("21008", userName, recipt.ToString(), "This receipt is from the production environment, but it was sent to the test environment for verification. Send it to the production environment instead.", appleResponse);
            //This receipt is from the production environment, but it was sent to the test environment for verification. Send it to the production environment instead.
            BsonDocument document = new BsonDocument() { { "status", false } };
            return document;
        }
        if (status == 0)
        {
            string bid = appleResponseDocument["receipt"]["bid"].ToString();
            if (bid == "com.compassgames.oraiarift")
            {
                ReciptHistoryRecord("0", userName, recipt.ToString(), "Recipt passed validation", appleResponse);
                BsonDocument document = new BsonDocument() { { "status", true }, { "t_id", appleResponseDocument["receipt"]["transaction_id"].ToString() }, { "p_id", appleResponseDocument["receipt"]["product_id"].ToString() } };
                return document;
            }
            else
            {
                ReciptHistoryRecord("0", userName, recipt.ToString(), "Recipt passed validation but invalid bundle identifier found", appleResponse);
                BsonDocument document = new BsonDocument() { { "status", false } };
                return document;
            }
            //For the recipts which passed validation or to show status of recipts which passed validation.
        }
        ReciptHistoryRecord("Unknown", userName, recipt.ToString(), "Unknown status encountered", appleResponse);
        BsonDocument unknown_status = new BsonDocument() { { "status", false } };
        return unknown_status;
    }

    //Reset password API, this will send an email
    [WebMethod]
    public string ForgetPassword(string username)
    {
        if (username == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username not passed" } };
            return returnDoc.ToString();
        }
        if (username.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username too short" } };
            return returnDoc.ToString();
        }

        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.And(Query.EQ("username", username), Query.EQ("TypeOfUser", FullmemberType));
        FieldsBuilder fields = Fields.Include("_id");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username with fullmembership does not exist" } };
            return returnDoc.ToString();

        }
        else
        {
            string password_token = GetUniqueKey();
            DateTime password_token_issuedTime = DateTime.Now;
            IMongoQuery query2 = Query.EQ("username", username);
            IMongoUpdate update = Update.Set("password_token", password_token).Set("password_token_issuedTime", password_token_issuedTime.ToString());
            usersCollection.Update(query2, update);
            if (IsValidEmail(username) == false)
            {
                BsonDocument returnDoc2 = new BsonDocument { { "status", false }, { "summary", "enter a valid Email ID" } };
                return returnDoc2.ToString();
            }
            SendEmail("Reset password", "Click on the following link to reset your password " + "http://oraiariftserver.azurewebsites.net/reset_password.aspx?password_token=" + password_token, username);
            BsonDocument returnDoc = new BsonDocument { { "status", true }, { "summary", "new password token issued" } };
            return returnDoc.ToString();
        }
    }

    //API to return the user purchased products
    [WebMethod]
    public string GetUserProducts(string token)
    {
        if (token.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token too short" } };
            return returnDoc.ToString();
        }
        string userID = ValidateToken(token);
        if (userID == "")
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "invalid token" } };
            return returnDoc.ToString();

        }

        else
        {

            MongoCollection usersCollection = database.GetCollection("UserProducts");
            IMongoQuery query = Query.EQ("userID", userID);
            FieldsBuilder fields = Fields.Include("ProductID").Include("ProductName");
            BsonDocument finalResult = new BsonDocument();
            MongoCursor<BsonDocument> filteredCollection = usersCollection.FindAs<BsonDocument>(query).SetFields(fields);
            finalResult.Add("status", "true");
            BsonDocument products = new BsonDocument();

            int i = 1;
            foreach (BsonDocument result in filteredCollection)
            {
                result.Remove("_id");
                products.Add("Product_" + i, result);
                i++;
            }
            finalResult.Add("ProductsList", products);
            return finalResult.ToString();
        }

    }

    //API to return end date and additional information about user subscription
    [WebMethod]
    public string GetSubscriptionDetails(string token)
    {
        if (token.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token too short" } };
            return returnDoc.ToString();
        }

        string userID = ValidateToken(token);
        if (userID == "")
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "invalid token" } };
            return returnDoc.ToString();

        }

        else
        {

            MongoCollection usersCollection = database.GetCollection("users");
            IMongoQuery query = Query.EQ("token", token);
            FieldsBuilder fields = Fields.Include("_id").Include("subscription_end_date");
            BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
            string dateAndTimeString = result["subscription_end_date"].ToString();

            DateTime subscriptionEndDate = DateTime.Parse(dateAndTimeString);
            DateTime today = DateTime.Today;

            if (DateTime.Compare(today, subscriptionEndDate) > 0)
            {
                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "Subscription end date for userID - " + userID + " is ", dateAndTimeString } };
                return returnDoc.ToString();

            }
            else                                              //if subscription is valid
            {

                BsonDocument returnDoc = new BsonDocument { { "status", true }, { "Subscription_end_date", dateAndTimeString } };
                return returnDoc.ToString();

            }
        }

    }

    //save files
    [WebMethod]
    public string Save(string token, string filename, string file_content)
    {
        if (token == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token not passed" } };
            return returnDoc.ToString();
        }
        if (token.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token too short" } };
            return returnDoc.ToString();
        }
        if (filename == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "filename not passed" } };
            return returnDoc.ToString();
        }
        if (filename.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "filename too short" } };
            return returnDoc.ToString();
        }
        if (file_content == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "file_content not passed" } };
            return returnDoc.ToString();
        }
        if (file_content.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "file_content too short" } };
            return returnDoc.ToString();
        }
        string userID = ValidateToken(token);
        if (userID == "")
        {

            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "invalid token" } };
            return returnDoc.ToString();

        }
        else
        {
            MongoCollection saved_files_Collection = database.GetCollection("saved_files");
            IMongoQuery query = Query.And(Query.EQ("userID", userID), Query.EQ("filename", filename));
            FieldsBuilder fields = Fields.Include("_id").Include("filename").Include("file_content");
            BsonDocument result = saved_files_Collection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();

            if (result == null)
            {
                BsonDocument document = new BsonDocument { { "userID", userID }, { "filename", filename }, { "file_content", file_content } };
                saved_files_Collection.Insert(document);
                BsonDocument returnDoc = new BsonDocument { { "status", true }, { "summary", "new file created" } };
                return returnDoc.ToString();

            }
            else
            {
                IMongoUpdate update = Update.Set("file_content", file_content);
                saved_files_Collection.Update(query, update);

                BsonDocument returnDoc = new BsonDocument { { "status", true }, { "summary", "file_content Updated" } };
                return returnDoc.ToString();
            }
        }
    }

    //user can save a file and load it, this is used to save/load player progress and game data, it is used to save diffrent slots, so user can have multiple files/progress/ sub players
    [WebMethod]
    public string Load(string token, string filename)
    {
        if (token == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token not passed" } };
            return returnDoc.ToString();
        }
        if (token.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token too short" } };
            return returnDoc.ToString();
        }
        if (filename == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "filename not passed" } };
            return returnDoc.ToString();
        }
        if (filename.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "filename too short" } };
            return returnDoc.ToString();
        }

        string userID = ValidateToken(token);
        if (userID == "")
        {

            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "invalid token" } };
            return returnDoc.ToString();

        }

        MongoCollection saved_files_Collection = database.GetCollection("saved_files");
        IMongoQuery query = Query.And(Query.EQ("userID", userID), Query.EQ("filename", filename));
        FieldsBuilder fields = Fields.Include("_id").Include("filename").Include("file_content");
        BsonDocument result = saved_files_Collection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "file not found" } };
            return returnDoc.ToString();

        }
        else
        {
            BsonDocument returnDoc = new BsonDocument { { "status", true }, { "file_content", result["file_content"] } };
            return returnDoc.ToString();
        }


    }

    //a public API to validate the token
    [WebMethod]
    public string ValidateUserToken(string token)
    {
        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.EQ("token", token);
        FieldsBuilder fields = Fields.Include("_id").Include("verified");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            BsonDocument document = new BsonDocument() { { "status", false } };
            return document.ToString();
        }
        BsonDocument returndoc2 = new BsonDocument() { { "status", true }, { "verified", result["verified"].ToBoolean() } };
        return returndoc2.ToString();
    }

    //resend verfication email to verifiy user email
    [WebMethod]
    public string ResendVerificationEmail(string username)
    {
        if (IsValidEmail(username) == false)
        {
            BsonDocument returnDoc2 = new BsonDocument { { "status", false }, { "error", "Enter a valid e-mail ID" } };
            return returnDoc2.ToString();
        }
        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.And(Query.EQ("username", username), Query.EQ("TypeOfUser", FullmemberType));
        FieldsBuilder fields = Fields.Include("_id");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault(); string today = DateTime.Today.ToString();
        if (result == null)
        {
            BsonDocument returndoc = new BsonDocument() { { "status", false }, { "error", "Username doesn't exist in database" } };
            return returndoc.ToString();
        }
        string verification_token = GetUniqueKey();
        IMongoUpdate update = Update.Set("verification_token", verification_token);
        usersCollection.Update(query, update);
        SendEmail("Successfully Registered", "Dear " + username + " ,Please click on the following link to confirm your registration- " + "http://oraiariftserver.azurewebsites.net/verify_e-mail.aspx?verification_token=" + verification_token, username);
        BsonDocument returndoc2 = new BsonDocument() { { "status", true } };
        return returndoc2.ToString();
    }

    //convert a guest memeber to full member with username and password
    [WebMethod]
    public string UpgradeGuestuserToFullmember(string guestUsername, string guestPassword, string newUsername, string newPassword)
    {

        if (guestUsername.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username too short" } };
            return returnDoc.ToString();
        }
        if (guestPassword.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "password too short" } };
            return returnDoc.ToString();
        }
        if (IsValidEmail(newUsername) == false)
        {
            BsonDocument returnDoc2 = new BsonDocument { { "status", false }, { "error", "Enter a valid e-mail ID" } };
            return returnDoc2.ToString();
        }
        if (newPassword.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "password too short" } };
            return returnDoc.ToString();
        }
        string encryptedPassword = EncodePasswordToBase64(guestPassword);
        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.And(Query.EQ("username", guestUsername), Query.EQ("TypeOfUser", GuestUserType));
        FieldsBuilder fields = Fields.Include("_id").Include("password");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Username not found" } };
            return returnDoc.ToString();
        }
        if (encryptedPassword == result["password"].ToString())
        {
            IMongoQuery query2 = Query.And(Query.EQ("username", newUsername), Query.EQ("TypeOfUser", FullmemberType));
            FieldsBuilder fields2 = Fields.Include("_id");
            BsonDocument result2 = usersCollection.FindAs<BsonDocument>(query2).SetFields(fields2).SetLimit(1).FirstOrDefault();
            if (result2 == null)
            {
                DateTime now = DateTime.Now;
                string token = GetUniqueKey();
                string verification_token = GetUniqueKey();
                IMongoUpdate update = Update.Set("username", newUsername).Set("password", EncodePasswordToBase64(newPassword))
                    .Set("TypeOfUser", FullmemberType).Set("token", token).Set("last_login", now.ToString()).Set("verification_token", verification_token).Set("verified", false);
                usersCollection.Update(query, update);
                SendEmail("Successfully Registered", "Dear " + newUsername + " ,Please click on the following link to confirm your upgradation- " + "http://oraiariftserver.azurewebsites.net/verify_e-mail.aspx?verification_token=" + verification_token, newUsername);
                BsonDocument returnDoc = new BsonDocument { { "status", true }, { "summary", "User successfuly upgraded from Guest usertype to Fullmember usertype" }, { "token", token } };
                return returnDoc.ToString();
            }
            else
            {
                BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "user name already exist" } };
                return returnDoc.ToString();
            }
        }

        BsonDocument returnDoc3 = new BsonDocument { { "status", false }, { "error", "Authentication failed" } };
        return returnDoc3.ToString();
    }

    //convert a guest user to a facebook user, so in future this user can use his facebook account to login
    [WebMethod]
    public string UpgradeGuestuserToFacebookmember(string guestUsername, string guestPassword, string facebookUserID, string facebookToken)
    {

        if (guestUsername.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username too short" } };
            return returnDoc.ToString();
        }
        if (guestPassword.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "password too short" } };
            return returnDoc.ToString();
        }
        if (facebookUserID.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "username too short" } };
            return returnDoc.ToString();
        }
        if (facebookToken.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "password too short" } };
            return returnDoc.ToString();
        }
        string encryptedPassword = EncodePasswordToBase64(guestPassword);
        MongoCollection usersCollection = database.GetCollection("users");
        IMongoQuery query = Query.And(Query.EQ("username", guestUsername), Query.EQ("TypeOfUser", GuestUserType));
        FieldsBuilder fields = Fields.Include("_id").Include("password");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Username not found" } };
            return returnDoc.ToString();
        }

        if (encryptedPassword == result["password"].ToString())
        {
            DateTime now = DateTime.Now;
            IMongoQuery query2 = Query.And(Query.EQ("username", facebookUserID), Query.EQ("TypeOfUser", FacebookUserType));
            FieldsBuilder fields2 = Fields.Include("_id");
            BsonDocument result2 = usersCollection.FindAs<BsonDocument>(query2).SetFields(fields2).SetLimit(1).FirstOrDefault();
            DateTime today = DateTime.Today;
            if (result2 == null)
            {
                BsonDocument facebookResult = ValidateFacebookUser(facebookUserID, facebookToken);
                if (facebookResult["status"].ToBoolean() == false)
                {
                    BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "facebook user not verified" } };
                    return returnDoc.ToString();
                }

                BsonDocument facebookResult2 = ValidateFacebookAppID(facebookToken);
                if (facebookResult2["status"].ToBoolean() == false)
                {
                    BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "facebook AppID not verified" } };
                    return returnDoc.ToString();
                }
                //facebook user verified.
                string token = GetUniqueKey();
                IMongoUpdate update = Update.Set("username", facebookUserID).Set("TypeOfUser", FacebookUserType).Set("token", token).Set("last_login", now.ToString());
                usersCollection.Update(query, update);
                BsonDocument returnDoc2 = new BsonDocument { { "status", true }, { "summary", "User successfuly upgraded from Guest usertype to Facebook usertype" }, { "token", token } };
                return returnDoc2.ToString();
            }


            BsonDocument returnDoc3 = new BsonDocument { { "status", false }, { "error", "Facebookuser already exist" } };
            return returnDoc3.ToString();


        }

        BsonDocument returnDoc4 = new BsonDocument { { "status", false }, { "error", "Authentication failed" } };
        return returnDoc4.ToString();
    }


    /// <summary>
    /// An API to return some numbers, it is used for internal statstics
    /// </summary>
    /// <returns>The statistics.</returns>
    /// <param name="pair_key">Pair key.</param>
    [WebMethod]
    public string Statistics(string pair_key)
    {
        if (pair_key.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "password_too_short" } };
            return returnDoc.ToString();
        }
        if (pair_key != "fdlg0i4huisngrsobg895")
        {
            BsonDocument returnDoc4 = new BsonDocument { { "status", false }, { "error", "Authentication failed" } };
            return returnDoc4.ToString();
        }

        MongoCollection usersCollection = database.GetCollection("users");           //for user's type data
        IMongoQuery query = Query.EQ("TypeOfUser", FullmemberType);
        IMongoQuery query2 = Query.EQ("TypeOfUser", FacebookUserType);
        IMongoQuery query3 = Query.EQ("TypeOfUser", GuestUserType);
        MongoCursor<BsonDocument> filteredCollection = usersCollection.FindAs<BsonDocument>(query);
        MongoCursor<BsonDocument> filteredCollection2 = usersCollection.FindAs<BsonDocument>(query2);
        MongoCursor<BsonDocument> filteredCollection3 = usersCollection.FindAs<BsonDocument>(query3);
        long count = filteredCollection.Count();
        long count2 = filteredCollection2.Count();
        long count3 = filteredCollection3.Count();


        DateTime currentTime = DateTime.Now.AddHours(-24);                                  //for apple recipt data
        MongoCollection usersCollection2 = database.GetCollection("AppleReciptsHistory");
        IMongoQuery query4 = Query.EQ("Status code", "0");
        MongoCursor<BsonDocument> filteredCollection4 = usersCollection2.FindAs<BsonDocument>(query4);
        long count4 = filteredCollection4.Count();
        IMongoQuery query5 = Query.And(Query.EQ("Status code", "0"), Query.GT("Time", currentTime));
        FieldsBuilder fields2 = Fields.Include("_id").Include("Time");
        MongoCursor<BsonDocument> filteredCollection5 = usersCollection2.FindAs<BsonDocument>(query5);
        long count5 = filteredCollection5.Count();

        MongoCollection usersCollection3 = database.GetCollection("LoginHistory");               // for login data
        IMongoQuery query6 = Query.And(Query.EQ("Summary", "Successfully logged in"), Query.GT("Time", currentTime));
        FieldsBuilder fields = Fields.Include("_id").Include("Time");
        MongoCursor<BsonDocument> filteredCollection6 = usersCollection3.FindAs<BsonDocument>(query6);
        long count6 = filteredCollection6.Count();

        BsonDocument result = new BsonDocument { { "number_of_fullmembers", count.ToString() },       //return result
        { "number_of_facebook_members", count2.ToString() },{ "number_of_guests", count3.ToString() },
        { "number_of_successful_recipts", count4.ToString() }, { "number_of_successful_recipts_in24hours", count5.ToString() },
        { "number_of_login_in_past_24hours", count6.ToString() }};
        return result.ToString();

    }

    //is this a valid subscription product on our database
    private int ValidateSubscriptionId(string ProductID)
    {
        MongoCollection usersCollection = database.GetCollection("SubscriptionPackages");


        IMongoQuery query = Query.EQ("ProductID", ProductID);
        FieldsBuilder fields = Fields.Include("_id").Include("Days");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            SendEmail("Unknown Subscription ProductID", "Product ID = " + ProductID, "info@shbli.com");
            return -1;

        }
        else
        {
            return result["Days"].ToInt32();

        }

    }

    //is this a valid product on our database
    private BsonDocument ValidateProduct(string ProductID)
    {

        MongoCollection usersCollection = database.GetCollection("Products");
        IMongoQuery query = Query.EQ("ProductID", ProductID);
        FieldsBuilder fields = Fields.Include("_id").Include("Name").Include("IsGift");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            SendEmail("Unknown Product ProductID", "Product ID = " + ProductID, "info@shbli.com");
            BsonDocument emptyString = new BsonDocument { { "status", false } };
            return emptyString;
        }
        else
        {
            BsonDocument emptyString = new BsonDocument { { "status", true }, { "Name", result["Name"].ToString() }, { "IsGift", result["IsGift"].ToBoolean() } };
            return emptyString;
        }
    }



    //an API to quickly figure out if a field value exist in a collection
    private bool ExistInCollection(MongoCollection collection, string fieldName, string fieldValue)
    {
        IMongoQuery query = Query.EQ(fieldName, fieldValue);

        FieldsBuilder fields = Fields.Include("_id");
        BsonDocument result = collection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            return false;
        }
        else
        {
            return true;
        }
    }


    private static string EncodePasswordToBase64(string password)
    {
        byte[] bytes = System.Text.Encoding.Unicode.GetBytes(password);
        byte[] inArray = HashAlgorithm.Create("SHA1").ComputeHash(bytes);
        return Convert.ToBase64String(inArray);
    }


    //Validate token and quickly return the userID, returns "" if the user is not valid
    private string ValidateToken(string token)
    {
        MongoCollection usersCollection = database.GetCollection("users");

        IMongoQuery query = Query.EQ("token", token);
        FieldsBuilder fields = Fields.Include("_id");
        BsonDocument result = usersCollection.FindAs<BsonDocument>(query).SetFields(fields).SetLimit(1).FirstOrDefault();
        if (result == null)
        {
            return "";
        }
        else
        {
            return result["_id"].ToString();

        }

    }

    //Validate the twitter credintials passed by the client, before allowing the user to login with his Twitter account, if valid the user can use "Log in with twitter feature", this is an internal API and not public
    private BsonDocument VerifyTwitterCredentials(string oauthconsumerkey, string oauthconsumersecret, string oauthtoken, string oauthtokensecret)
    {
        string oauthsignaturemethod = "HMAC-SHA1";
        string oauthversion = "1.0";

        string oauthnonce = Convert.ToBase64String(new ASCIIEncoding().GetBytes(DateTime.Now.Ticks.ToString()));
        TimeSpan ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        string oauthtimestamp = Convert.ToInt64(ts.TotalSeconds).ToString();
        SortedDictionary<string, string> sd = new SortedDictionary<string, string>();
        sd.Add("oauth_version", "1.0");
        sd.Add("oauth_consumer_key", oauthconsumerkey);
        sd.Add("oauth_nonce", oauthnonce);
        sd.Add("oauth_signature_method", "HMAC-SHA1");
        sd.Add("oauth_timestamp", oauthtimestamp);
        sd.Add("oauth_token", oauthtoken);
        //GS - Build the signature string
        string baseString = String.Empty;
        baseString += "GET" + "&";
        baseString += Uri.EscapeDataString(
          "https://api.twitter.com/1.1/account/verify_credentials.json") + "&";
        foreach (KeyValuePair<string, string> entry in sd)
        {
            baseString += Uri.EscapeDataString(entry.Key + "=" + entry.Value + "&");
        }

        //Remove the trailing ambersand char(last 3 chars - %26)
        baseString = baseString.Substring(0, baseString.Length - 3);

        //Build the signing key
        string signingKey = Uri.EscapeDataString(oauthconsumersecret) +
          "&" + Uri.EscapeDataString(oauthtokensecret);

        //Sign the request
        HMACSHA1 hasher = new HMACSHA1(new ASCIIEncoding().GetBytes(signingKey));
        string oauthsignature = Convert.ToBase64String(
          hasher.ComputeHash(new ASCIIEncoding().GetBytes(baseString)));

        //Tell Twitter we don't do the 100 continue thing
        ServicePointManager.Expect100Continue = false;

        //authorization header
        HttpWebRequest hwr = (HttpWebRequest)WebRequest.Create(
          @"https://api.twitter.com/1.1/account/verify_credentials.json");
        string authorizationHeaderParams = String.Empty;
        authorizationHeaderParams += "OAuth ";
        authorizationHeaderParams += "oauth_nonce=" + "\"" +
          Uri.EscapeDataString(oauthnonce) + "\",";
        authorizationHeaderParams += "oauth_signature_method=" + "\"" +
          Uri.EscapeDataString(oauthsignaturemethod) + "\",";
        authorizationHeaderParams += "oauth_timestamp=" + "\"" +
          Uri.EscapeDataString(oauthtimestamp) + "\",";
        authorizationHeaderParams += "oauth_consumer_key=" + "\"" +
          Uri.EscapeDataString(oauthconsumerkey) + "\",";
        authorizationHeaderParams += "oauth_token=" + "\"" +
          Uri.EscapeDataString(oauthtoken) + "\",";
        authorizationHeaderParams += "oauth_signature=" + "\"" +
          Uri.EscapeDataString(oauthsignature) + "\",";
        authorizationHeaderParams += "oauth_version=" + "\"" +
          Uri.EscapeDataString(oauthversion) + "\"";
        hwr.Headers.Add("Authorization", authorizationHeaderParams);
        hwr.Method = "GET";
        hwr.ContentType = "application/x-www-form-urlencoded";

        //Allow us a reasonable timeout in case Twitter's busy
        hwr.Timeout = 3 * 60 * 1000;
        try
        {
            HttpWebResponse rsp = hwr.GetResponse() as HttpWebResponse;
            Stream dataStream = rsp.GetResponseStream();
            //Open the stream using a StreamReader for easy access.
            StreamReader reader = new StreamReader(dataStream);
            //Read the content.
            string responseFromServer = reader.ReadToEnd();
            BsonDocument returnDoc = new BsonDocument() { { "status", true }, { "twitterResponse", responseFromServer } };
            return returnDoc;
        }
        catch (Exception ex)
        {
            BsonDocument returnDoc = new BsonDocument() { { "status", false }, { "error", ex.ToString() } };
            return returnDoc;
        }
    }

    //http post request quick API
    private static string PostRequest(string url, byte[] byteArray)
    {
        try
        {
            WebRequest request = HttpWebRequest.Create(url);
            request.Method = "POST";
            request.ContentLength = byteArray.Length;
            request.ContentType = "application/json";

            using (System.IO.Stream dataStream = request.GetRequestStream())
            {
                dataStream.Write(byteArray, 0, byteArray.Length);
                dataStream.Close();
            }

            using (WebResponse r = request.GetResponse())
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(r.GetResponseStream()))
                {
                    return sr.ReadToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }

    //gift users for sharing on social media channels, for now the validation is done on client, to be imroved in the future to validate users social media actions on server
    [WebMethod]
    public string ProcessGift(string token, string productID)
    {
        if (token.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "token too short" } };
            return returnDoc.ToString();
        }

        if (productID.Length < 3)
        {
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "productID too short" } };
            return returnDoc.ToString();
        }
        string userID = ValidateToken(token);
        if (userID == "")
        {
            UserProductHistory("Purchasing failed, invalid token", "UNABLE TO RETRIVE USERNAME", token, "");
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "invalid token" } };
            return returnDoc.ToString();
        }
        BsonDocument GiftOrNot = ValidateProduct(productID);
        bool IsGift = GiftOrNot["IsGift"].ToBoolean();
        if (IsGift == false)
        {
            UserProductHistory("Purchase failed,product chosen was not a gift", userID, token, productID);
            BsonDocument returnDoc = new BsonDocument { { "status", false }, { "error", "Product is not a Gift" } };
            return returnDoc.ToString();
        }
        MongoCollection BuyItemsCollection = database.GetCollection("UserProducts");
        BsonDocument document = new BsonDocument { { "userID", userID }, { "ProductID", productID }, { "ProductName", GiftOrNot["Name"].ToString() } };
        BuyItemsCollection.Insert(document);
        UserProductHistory("Purchase successful", userID, token, productID);
        BsonDocument returnDoc2 = new BsonDocument { { "status", true }, { "Product", GiftOrNot["Name"].ToString() } };
        return returnDoc2.ToString();
    }

    //add to a history collection, this is used for loggin user actions and customer service in case something goes wrong, tracking user actions are useful
    private void ReciptHistoryRecord(string statusCode, string userID, string Recipt, string Description, string apple_response)
    {
        MongoCollection usersCollection = database.GetCollection("AppleReciptsHistory");
        BsonDocument document = new BsonDocument { { "Time", DateTime.Now }, { "userID", userID }, { "Recipt", Recipt }, { "Status code", statusCode }, { "Description", Description }, { "Apple response", apple_response } };
        usersCollection.Insert(document);
        SendEmail("New apple recipt processed", document.ToString(), "info@shbli.com");
    }

    //add to a history collection, this is used for loggin user actions and customer service in case something goes wrong, tracking user actions are useful
    private void RegistrationHistory(string Summary, string userID, string usertype, string Token)
    {
        MongoCollection RegistrationHistory = database.GetCollection("RegistrationHistory");
        BsonDocument document = new BsonDocument { { "Time", DateTime.Now }, { "Summary", Summary }, { "userID", userID }, { "UserType", usertype }, { "Token", Token } };
        RegistrationHistory.Insert(document);
    }

    //add to a history collection, this is used for loggin user actions and customer service in case something goes wrong, tracking user actions are useful
    private void LoginHistory(string Summary, string userID, string usertype, string Token)
    {
        MongoCollection loginhistorycollection = database.GetCollection("LoginHistory");
        BsonDocument document = new BsonDocument { { "Time", DateTime.Now }, { "Summary", Summary }, { "userID", userID }, { "UserType", usertype }, { "Token", Token } };
        loginhistorycollection.Insert(document);
    }


    //add to a history collection, this is used for loggin user actions and customer service in case something goes wrong, tracking user actions are useful
    private void SubscriptionHistory(string Summary, string userID, string Token, string ProductID)
    {
        MongoCollection subscriptionhistorycollection = database.GetCollection("SubscriptionHistory");
        BsonDocument document = new BsonDocument { { "Time", DateTime.Now }, { "Summary", Summary }, { "userID", userID }, { "Token", Token }, { "RequestedPackage", ProductID } };
        subscriptionhistorycollection.Insert(document);
    }

    //add to a history collection, this is used for loggin user actions and customer service in case something goes wrong, tracking user actions are useful
    private void UserProductHistory(string Summary, string userID, string Token, string ProductID)
    {
        MongoCollection userproducthistorycollection = database.GetCollection("UserProductsHistory");
        BsonDocument document = new BsonDocument { { "Time", DateTime.Now }, { "Summary", Summary }, { "userID", userID }, { "Token", Token }, { "RequestedPackage", ProductID } };
        userproducthistorycollection.Insert(document);
    }

    private static string Base64Encode(string plainText)
    {
        var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return System.Convert.ToBase64String(plainTextBytes);
    }

    private static string Base64Decode(string encodedString)
    {
        byte[] data = Convert.FromBase64String(encodedString);
        return Encoding.UTF8.GetString(data);
    }

    //send an email to using the game email address noreply@oraiarift.com
    private string SendEmail(string subject, string body, string toEmail)
    {
        MailMessage objeto_mail = new MailMessage();
        SmtpClient client = new SmtpClient();
        client.Port = 2525;
        client.Host = "smtp.oraiarift.com";
        client.Timeout = 10000;
        client.DeliveryMethod = SmtpDeliveryMethod.Network;
        client.UseDefaultCredentials = false;
        client.Credentials = new System.Net.NetworkCredential("noreply@oraiarift.com", "Q6*3Ghx&deY5@");
        objeto_mail.From = new MailAddress("noreply@oraiarift.com");
        objeto_mail.To.Add(new MailAddress(toEmail));
        objeto_mail.Subject = subject;
        objeto_mail.Body = body;
        client.Send(objeto_mail);
        return "Success";
    }

    //validate an email string
    private static bool IsValidEmail(string inputEmail)
    {
        string strRegex = @"^([a-zA-Z0-9_\-\.]+)@((\[[0-9]{1,3}" +
              @"\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([a-zA-Z0-9\-]+\" +
              @".)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$";
        Regex re = new Regex(strRegex);
        if (re.IsMatch(inputEmail))
            return (true);
        else
            return (false);
    }

    //Generate a random unique key
    private string GetUniqueKey()
    {
        int maxSize = 32;

        char[] chars = new char[62];
        string a;
        a = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        chars = a.ToCharArray();
        int size = maxSize;
        byte[] data = new byte[1];
        RNGCryptoServiceProvider crypto = new RNGCryptoServiceProvider();
        crypto.GetNonZeroBytes(data);
        size = maxSize;
        data = new byte[size];
        crypto.GetNonZeroBytes(data);
        StringBuilder result = new StringBuilder(size);
        foreach (byte b in data)
        {
            result.Append(chars[b % (chars.Length)]);
        }
        return result.ToString();
    }

    //This is an internal API to validate a user facebook user againest a token
    private BsonDocument ValidateFacebookUser(string facebookUserID, string facebookToken)
    {
        try
        {
            WebRequest request = HttpWebRequest.Create("https://graph.facebook.com/me?access_token=" + facebookToken);
            request.Method = "GET";
            request.ContentType = "text/plain";


            using (WebResponse r = request.GetResponse())
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(r.GetResponseStream()))
                {
                    BsonDocument checkResult = BsonDocument.Parse(sr.ReadToEnd());
                    string userid = checkResult["id"].ToString();
                    if (facebookUserID == userid)
                    {
                        BsonDocument returnDoc = new BsonDocument() { { "status", true } };
                        return returnDoc;
                    }
                    else
                    {
                        BsonDocument returnDoc = new BsonDocument() { { "status", false }, { "error", "Incorrect UserID encountered" } };
                        return returnDoc;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BsonDocument returnDoc = new BsonDocument() { { "status", false }, { "error", ex.ToString() } };
            return returnDoc;
        }
    }

    //This is an internal API to validate user facebookToken
    private BsonDocument ValidateFacebookAppID(string facebookToken)
    {
        try
        {
            WebRequest request = HttpWebRequest.Create("https://graph.facebook.com/app/?access_token=" + facebookToken);
            request.Method = "GET";
            request.ContentType = "text/plain";


            using (WebResponse r = request.GetResponse())
            {
                using (System.IO.StreamReader sr = new System.IO.StreamReader(r.GetResponseStream()))
                {
                    BsonDocument checkResult = BsonDocument.Parse(sr.ReadToEnd());
                    string Appid = checkResult["id"].ToString();
                    if (facebookAppID == Appid)
                    {
                        BsonDocument returnDoc = new BsonDocument() { { "status", true } };
                        return returnDoc;
                    }
                    else
                    {
                        BsonDocument returnDoc = new BsonDocument() { { "status", false }, { "error", "Incorrect AppID encountered" } };
                        return returnDoc;
                    }

                }
            }
        }
        catch (Exception ex)
        {
            BsonDocument returnDoc = new BsonDocument() { { "status", false }, { "error", ex.ToString() } };
            return returnDoc;
        }
    }
}