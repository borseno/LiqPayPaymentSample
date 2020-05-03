module GiraffeWebApp.App

open System
open Newtonsoft.Json
open System.IO
open System.Text
open System.Net.Http
open System.Security.Cryptography
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Microsoft.AspNetCore.Http
open Telegram.Bot
open Telegram.Bot.Types

// ---------------------------------
// Models
// ---------------------------------

type Message =
    {
        Text : string
    }

[<CLIMutable>]
type LiqPayClientModel = {
    [<JsonProperty("public_key")>]     PublicKey   : string
    [<JsonProperty("version")>]        Version     : int
    [<JsonProperty("action")>]         Action      : string
    [<JsonProperty("amount")>]         Amount      : uint32
    [<JsonProperty("currency")>]       Currency    : string
    [<JsonProperty("description")>]    Description : string
    [<JsonProperty("order_id")>]       OrderId     : string
    [<JsonProperty("server_url")>]     CallbackURL : string
    }
// ---------------------------------
// Views
// ---------------------------------

[<CLIMutable>]
type LiqPayCallbackData = {
    [<JsonProperty("public_key")>]     PublicKey     : string
    [<JsonProperty("amount")>]         Amount        : uint32
    [<JsonProperty("currency")>]       Currency      : string
    [<JsonProperty("description")>]    Description   : string
    [<JsonProperty("type")>]           Type          : string
    [<JsonProperty("status")>]         Status        : string
    [<JsonProperty("signature")>]      Signature     : string
    [<JsonProperty("order_id")>]       OrderId       : string
    [<JsonProperty("transaction_id")>] TransactionId : string
    [<JsonProperty("sender_phone")>]   SenderPhone   : string
}

[<CLIMutable>]
type LiqPayCallbackModel = {
    [<JsonProperty("data")>] Data : string
    [<JsonProperty("signature")>] Signature : string
}

let getSignature data =
        let privateKey = "sandbox_awM5L8XANAMkM41IfwUcH51gVsaKpXEgmpuSty2k"
        privateKey + data + privateKey
        |> Encoding.UTF8.GetBytes
        |> SHA1.Create().ComputeHash
        |> Convert.ToBase64String
    
let getFormValues () =
    let currentURL = "https://paymentsite.herokuapp.com/callback"
    let publicKey = "sandbox_i48609436030"
    let json = {
        PublicKey = publicKey
        Version = 3
        Action = "pay"
        Amount = 4u
        Currency = "UAH"
        Description = "Giraffe payment"
        OrderId = Guid.NewGuid().ToString("N")
        CallbackURL = currentURL
    }
    let data =
        json
        |> JsonConvert.SerializeObject
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String
    
    let signature = getSignature data
    
    signature, data

module Views =
    open GiraffeViewEngine

    let layout (content: XmlNode list) =
        html [] [
            head [] [
                title []  [ encodedText "GiraffeWebApp" ]
                link [ _rel  "stylesheet"
                       _type "text/css"
                       _href "/main.css" ]
            ]
            body [] content
        ]
          
    let partial () =
        let signature, data = getFormValues ()        
        h1 [] [ encodedText "GiraffeWebApp" ]
        form [_method "POST"; _action "https://www.liqpay.ua/api/3/checkout"; _acceptCharset "utf-8"] [
            input [_type "hidden"; _name "data"; _value ( data )];
            input [_type "hidden"; _name "signature"; _value ( signature )]
            input [_type "image"; _src @"//static.liqpay.ua/buttons/p1ru.radius.png"]
        ]

    let index (model : Message) =
        [
            partial()
            p [] [ encodedText model.Text ]
        ] |> layout

// ---------------------------------
// Web app
// ---------------------------------

let getDataFromModel model =
        model.Data
        |> Convert.FromBase64String
        |> Encoding.UTF8.GetString
        |> JsonConvert.DeserializeObject<LiqPayCallbackData>

let verifySignature (callbackModel:LiqPayCallbackModel) =
    let data = getDataFromModel callbackModel
        
    let givenSignature = callbackModel.Signature
    let ourSignature =
        data
        |> JsonConvert.SerializeObject
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String
        |> getSignature
    
    ourSignature = givenSignature

let writeToTelgramBot str =
    let token = Environment.GetEnvironmentVariable("telegrambot_token")
    let adminChatId = Int64.Parse(Environment.GetEnvironmentVariable("adminchatid"))
    let client = TelegramBotClient(token)
    client.SendTextMessageAsync(ChatId(adminChatId), str).GetAwaiter().GetResult() |> ignore
    
    ()

let callbackHandler (model:LiqPayCallbackModel) =
    let data = getDataFromModel model
    let answer = if verifySignature model then
                    (sprintf "Thank you for the payment of %i bucks, dear %s!" data.Amount data.SenderPhone)
                 else "Sorry payment is bad"
                 
    writeToTelgramBot (JsonConvert.SerializeObject(data))
    writeToTelgramBot answer
    
    let model     = { Text = answer }
    let view      = Views.index model
    htmlView view
    
let indexHandler (name : string) =
    let greetings = sprintf "Hello %s, from Giraffe!" name
    let model     = { Text = greetings }
    let view      = Views.index model
    htmlView view

let webApp =
    choose [
        GET >=>
            choose [
                route "/" >=> indexHandler "world"
                routef "/hello/%s" indexHandler
            ]
        POST >=> choose [
            route "/callback" >=> bindModel<LiqPayCallbackModel> None callbackHandler
        ]
        setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080")
           .AllowAnyMethod()
           .AllowAnyHeader()
           |> ignore

let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IHostingEnvironment>()
    (match env.IsDevelopment() with
    | true  -> app.UseDeveloperExceptionPage()
    | false -> app.UseGiraffeErrorHandler errorHandler)
        .UseHttpsRedirection()
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(webApp)

let configureServices (services : IServiceCollection) =
    services.AddCors()    |> ignore
    services.AddGiraffe() |> ignore

let configureLogging (builder : ILoggingBuilder) =
    builder.AddFilter(fun l -> l.Equals LogLevel.Error)
           .AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main _ =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    WebHostBuilder()
        .UseKestrel()
        .UseContentRoot(contentRoot)
        .UseIISIntegration()
        .UseWebRoot(webRoot)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .Build()
        .Run()
    0
