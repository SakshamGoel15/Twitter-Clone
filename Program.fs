open System
open Akka.FSharp
open Akka.Configuration
open FSharp.Json
open Akka.Actor
open Suave
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Sockets
open Suave.Sockets.Control
open Suave.WebSocket
open Newtonsoft.Json
open Newtonsoft.Json.Serialization
let configuration = 
    ConfigurationFactory.ParseString(
            @"akka {
            stdout-loglevel = off
            loglevel = ERROR
            log-dead-letters-during-shutdown = off
        }")
let mutable Tags_Mapping = Map.empty
let mutable Mention_Mapping = Map.empty
let mutable Registering_User_Mapping = Map.empty
let mutable User_Follower_Mapping = Map.empty
   (*
    * Defining server Actor model system Globally 
    *)
let system = ActorSystem.Create("TwitterServer")

   (*
    * Defining Response Type
    *)

type ResponseType = {
  userName: string
  responseMessage: string
  service: string
  code: string
}

   (*
    * Defining Request Type 
    *)

type RequestType = {
  userName: string
  value: string
}
    (*
    * Defining Tweet_Message 
    *)

type Tweet_Message = 
  | InitTweet of (IActorRef)
  | TweetRequest of (string*string*string)
  | AddRegisteredUser of (string)
   
   (*
    * Defining Showfeed 
    *)

type Showfeed = 
  | Register_User of (string)
  | User_subscribers of (string*string)
  | RemovecurrentActiveUser of (string)
  | UpdateFeeds of (string*string*string)
  | AddcurrentActiveUser of (string*WebSocket)
   
  (*
   * Defining Check_RequestType (checking request type)
   *)

type Check_RequestType<'a> = { Entry : RequestType -> string }

  (*
   * worker Sending Response on websbsocket
   *)

let worker = MailboxProcessor<string*WebSocket>.Start(fun data ->
  let rec Message_Loop() = async {
    let! msg,websocket = data.Receive()
    let response_Byte =
      msg
      |> System.Text.Encoding.ASCII.GetBytes
      |> ByteSegment
    let! _ = websocket.send Text response_Byte true
    return! Message_Loop()
  }
  Message_Loop()
)

  (*
   * supervisor (controling requests )
   *)

let SupervisorActorFeed (mailbox:Actor<_>) = 
  let mutable follow = Map.empty
  let mutable count:int  = 0
  let mutable currentActiveUser = Map.empty
  let mutable value:int = 0
  let mutable bool = false
  let mutable Table_Feed = Map.empty
  let rec loop () = actor {
      let! responseMessage = mailbox.Receive() 
      match responseMessage with
      | Register_User(userName) ->
        count <- count+1
        follow <- Map.add userName Set.empty follow
        Table_Feed <- Map.add userName List.empty Table_Feed
      | UpdateFeeds(userName,tweetMsg,type_Service) ->
        if follow.ContainsKey userName then
          let mutable stype = ""
          if type_Service = "Tweet" then
            stype <- sprintf "%s tweeted:" userName
          else 
            stype <- sprintf "%s re-tweeted:" userName
          for foll in follow.[userName] do 
            if follow.ContainsKey foll then
              if currentActiveUser.ContainsKey foll then
                let twt = sprintf "%s^%s" stype tweetMsg
                let jsonData: ResponseType = {userName = foll; service=type_Service; code="OK"; responseMessage = twt}
                let jsonCoverter = Json.serialize jsonData
                worker.Post (jsonCoverter,currentActiveUser.[foll])
              let mutable listy = []
              if Table_Feed.ContainsKey foll then
                  listy <- Table_Feed.[foll]
              listy  <- (sprintf "%s^%s" stype tweetMsg) :: listy
              Table_Feed <- Map.remove foll Table_Feed
              Table_Feed <- Map.add foll listy Table_Feed
      | User_subscribers(userName, follow_ID) ->
        if follow.ContainsKey follow_ID then
          let mutable set_Follow = Set.empty
          set_Follow <- follow.[follow_ID]
          set_Follow <- Set.add userName set_Follow
          follow <- Map.remove follow_ID follow 
          follow <- Map.add follow_ID set_Follow follow
          let mutable jsonData: ResponseType = {userName = follow_ID; service= "Follow"; code = "OK"; responseMessage = sprintf "Congrates!! User %s started following you!" userName}
          let mutable jsonCoverter = Json.serialize jsonData
          worker.Post (jsonCoverter,currentActiveUser.[follow_ID])
      | AddcurrentActiveUser(userName,userWebsocket) ->
        bool<-true
        if currentActiveUser.ContainsKey userName then  
          currentActiveUser <- Map.remove userName currentActiveUser
        currentActiveUser <- Map.add userName userWebsocket currentActiveUser 
        let mutable posted_Feed = ""
        let mutable type_Service = ""
        if Table_Feed.ContainsKey userName then
          let mutable feedsTop = ""
          let mutable fSize = 10
          let feedList:List<string> = Table_Feed.[userName]
          if feedList.Length = 0 then
            type_Service <- "start Follow"
            posted_Feed <- sprintf "No feeds found."
          else
            if feedList.Length < 10 then
                fSize <- feedList.Length
            for i in [0..(fSize-1)] do
              feedsTop <- "-" + Table_Feed.[userName].[i] + feedsTop
            posted_Feed <- feedsTop
            type_Service <- "LiveFeed"
          let jsonData: ResponseType = {userName = userName; responseMessage = posted_Feed; code = "OK"; service=type_Service}
          let jsonCoverter = Json.serialize jsonData
          worker.Post (jsonCoverter,userWebsocket) 
      | RemovecurrentActiveUser(userName) ->
        if currentActiveUser.ContainsKey userName then 
          value <- value + count + 1 
          currentActiveUser <- Map.remove userName currentActiveUser
      
      return! loop()
  }
  loop()

let supervisorActorFeed = spawn system (sprintf "SupervisorActorFeed") SupervisorActorFeed

let liveFeed (webSocket : WebSocket) (context: HttpContext) =
  let rec loop() =
    let mutable currentUser = ""
    socket { 
      let! msg = webSocket.read()
      match msg with
      | (Close, _, _) ->
        printfn " Websocket closed"
        supervisorActorFeed <! RemovecurrentActiveUser(currentUser)
        let emptyResponse = [||] |> ByteSegment
        do! webSocket.send Close emptyResponse true

      | (Text, data, true) ->
        let requestMsg = UTF8.toString data
        let par = Json.deserialize<RequestType> requestMsg
        printfn "%A" par
        currentUser <- par.userName
        supervisorActorFeed <! AddcurrentActiveUser(par.userName, webSocket)
        return! loop()
      | _ -> return! loop()
      
    }
  loop()

let requestUser userInput =
  printfn "%A" userInput
  let mutable response = ""
  if Registering_User_Mapping.ContainsKey userInput.userName then
    let receivertype: ResponseType = {userName = userInput.userName; responseMessage = sprintf "User %s already registred" userInput.userName; service = "Register"; code = "FAIL"}
    response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  else
    Registering_User_Mapping <- Map.add userInput.userName userInput.value Registering_User_Mapping
    User_Follower_Mapping <- Map.add userInput.userName Set.empty User_Follower_Mapping
    supervisorActorFeed <! Register_User(userInput.userName)
    let receivertype: ResponseType = {userName = userInput.userName; responseMessage = sprintf "User %s registred successfully" userInput.userName; service = "Register"; code = "OK"}
    response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  response  

let loginUser userInput =
  let mutable response = ""
  if Registering_User_Mapping.ContainsKey userInput.userName then
    if Registering_User_Mapping.[userInput.userName] = userInput.value then
      let receivertype: ResponseType = {userName = userInput.userName; responseMessage = sprintf "User %s logged in successfully" userInput.userName; service = "Login"; code = "OK"}
      response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
    else 
      let receivertype: ResponseType = {userName = userInput.userName; responseMessage = "Invalid userName or password"; service = "Login"; code = "FAIL"}
      response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  else
    let receivertype: ResponseType = {userName = userInput.userName; responseMessage = "Invalid userName or password"; service = "Login"; code = "FAIL"}
    response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  response

let tweetUser userInput =
  let mutable response = ""
  if Registering_User_Mapping.ContainsKey userInput.userName then
    let mutable Tag = ""
    let mutable User_men = ""
    let parsed = userInput.value.Split ' '
    // printfn "parsed = %A" parsed
    for parse in parsed do
      if parse.Length > 0 then
        if parse.[0] = '#' then
          Tag <- parse.[1..(parse.Length-1)]
        else if parse.[0] = '@' then
          User_men <- parse.[1..(parse.Length-1)]

    if User_men <> "" then
      if Registering_User_Mapping.ContainsKey User_men then
        if not (Mention_Mapping.ContainsKey User_men) then
            Mention_Mapping <- Map.add User_men List.empty Mention_Mapping
        let mutable mList = Mention_Mapping.[User_men]
        mList <- (sprintf "%s tweeted:^%s" userInput.userName userInput.value) :: mList
        Mention_Mapping <- Map.remove User_men Mention_Mapping
        Mention_Mapping <- Map.add User_men mList Mention_Mapping
        supervisorActorFeed <! UpdateFeeds(userInput.userName,userInput.value,"Tweet")
        let receivertype: ResponseType = {userName = userInput.userName; service="Tweet"; responseMessage = (sprintf "%s tweeted:-> %s" userInput.userName userInput.value); code = "OK"}
        response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
      else
        let receivertype: ResponseType = {userName = userInput.userName; service="Tweet"; responseMessage = sprintf "request Is Invalid, mentioned user (%s) is not registered" User_men; code = "FAIL"}
        response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
    else
      supervisorActorFeed <! UpdateFeeds(userInput.userName,userInput.value,"Tweet")
      let receivertype: ResponseType = {userName = userInput.userName; service="Tweet"; responseMessage = (sprintf "%s tweeted:->%s" userInput.userName userInput.value); code = "OK"}
      response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString

    if Tag <> "" then
      if not (Tags_Mapping.ContainsKey Tag) then
        Tags_Mapping <- Map.add Tag List.empty Tags_Mapping
      let mutable tList = Tags_Mapping.[Tag]
      tList <- (sprintf "%s tweeted:->%s" userInput.userName userInput.value) :: tList
      Tags_Mapping <- Map.remove Tag Tags_Mapping
      Tags_Mapping <- Map.add Tag tList Tags_Mapping
  else  
    let receivertype: ResponseType = {userName = userInput.userName; service="Tweet"; responseMessage = sprintf "Request is Invalid by user %s, Not registered yet!" userInput.userName; code = "FAIL"}
    response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  response

let followUser userInput =
  let mutable response = ""
  if userInput.value <> userInput.userName then
    if User_Follower_Mapping.ContainsKey userInput.value then
      if not (User_Follower_Mapping.[userInput.value].Contains userInput.userName) then
        let mutable tempset = User_Follower_Mapping.[userInput.value]
        tempset <- Set.add userInput.userName tempset
        User_Follower_Mapping <- Map.remove userInput.value User_Follower_Mapping
        User_Follower_Mapping <- Map.add userInput.value tempset User_Follower_Mapping
        supervisorActorFeed <! User_subscribers(userInput.userName,userInput.value) 
        let receivertype: ResponseType = {userName = userInput.userName; service="Follow"; responseMessage = sprintf "Congrats!! You started following %s!" userInput.value; code = "OK"}
        response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
      else 
        let receivertype: ResponseType = {userName = userInput.userName; service="Follow"; responseMessage = sprintf "You are already following %s!" userInput.value; code = "FAIL"}
        response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString      
    else  
      let receivertype: ResponseType = {userName = userInput.userName; service="Follow"; responseMessage = sprintf "Request is Invalid, No such user (%s)." userInput.value; code = "FAIL"}
      response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  else
    let receivertype: ResponseType = {userName = userInput.userName; service="Follow"; responseMessage = sprintf "How can you follow yourself?"; code = "FAIL"}
    response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString    
  response

let re_tweet userInput =
  let mutable response= ""
  if Registering_User_Mapping.ContainsKey userInput.userName then
    supervisorActorFeed <! UpdateFeeds(userInput.userName,userInput.value,"ReTweet")
    let receivertype: ResponseType = {userName = userInput.userName; service="ReTweet"; responseMessage = (sprintf "%s re-tweeted:->%s" userInput.userName userInput.value); code = "OK"}
    response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  else  
    let receivertype: ResponseType = {userName = userInput.userName; service="ReTweet"; responseMessage = sprintf "Request is Invalid by user %s, Not registered yet!" userInput.userName; code = "FAIL"}
    response <- receivertype |> Json.toJson |> System.Text.Encoding.UTF8.GetString
  response

let query (userInput:string) = 
  let mutable tagsstring = ""
  let mutable mentionsString = ""
  let mutable response = ""
  let mutable size = 10
  if userInput.Length > 0 then
    if userInput.[0] = '@' then
      let searchKey = userInput.[1..(userInput.Length-1)]
      if Mention_Mapping.ContainsKey searchKey then
        let mapData:List<string> = Mention_Mapping.[searchKey]
        if (mapData.Length < 10) then
          size <- mapData.Length
        for i in [0..(size-1)] do
          mentionsString <- mentionsString + "-" + mapData.[i]
        let receivertype: ResponseType = {userName = ""; service="Query"; responseMessage = mentionsString; code = "OK"}
        response <- Json.serialize receivertype
      else 
        let receivertype: ResponseType = {userName = ""; service="Query"; responseMessage = "There is no tweets found for the user mentioned"; code = "OK"}
        response <- Json.serialize receivertype
    else
      let searchKey = userInput
      if Tags_Mapping.ContainsKey searchKey then
        let mapData:List<string> = Tags_Mapping.[searchKey]
        if (mapData.Length < 10) then
            size <- mapData.Length
        for i in [0..(size-1)] do
            tagsstring <- tagsstring + "-" + mapData.[i]
        let receivertype: ResponseType = {userName = ""; service="Query"; responseMessage = tagsstring; code = "OK"}
        response <- Json.serialize receivertype
      else 
        let receivertype: ResponseType = {userName = ""; service="Query"; responseMessage = "There is no tweets found for the Tag"; code = "OK"}
        response <- Json.serialize receivertype
  else
    let receivertype: ResponseType = {userName = ""; service="Query"; responseMessage = "Type something to search"; code = "FAIL"}
    response <- Json.serialize receivertype
  response

let json_Response v =     
    let jsonSerializerSettings = JsonSerializerSettings()
    jsonSerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
    JsonConvert.SerializeObject(v, jsonSerializerSettings)
    |> OK 
    >=> Writers.setMimeType "application/json; charset=utf-8"
let response_Json (v:string) =     
    let jsonSerializerSettings = JsonSerializerSettings()
    jsonSerializerSettings.ContractResolver <- CamelCasePropertyNamesContractResolver()
    JsonConvert.SerializeObject(v, jsonSerializerSettings)
    |> OK 
    >=> Writers.setMimeType "application/json; charset=utf-8"
let response_From_Json<'a> json =
        JsonConvert.DeserializeObject(json, typeof<'a>) :?> 'a 

let get_Request<'a> (requestInp : HttpRequest) = 
    let getInString (rawForm:byte[]) = System.Text.Encoding.UTF8.GetString(rawForm)
    let Arrayrequest:byte[] = requestInp.rawForm
    Arrayrequest |> getInString |> response_From_Json<RequestType>

let entryRequest resourceName resource = 
  let resourcePath = "/" + resourceName
  let entryDone userInput =
    let userRegResp = resource.Entry userInput
    userRegResp
  choose [
    path resourcePath >=> choose [
      POST >=> request (get_Request >> entryDone >> json_Response)
    ]
  ]
   (*
    * Defining entry Point
    *)
let Register_UserPoint = entryRequest "newregisteration" { Entry = requestUser }
let LoginUserPoint = entryRequest "login" { Entry = loginUser }
let FollowUserPoint = entryRequest "Userfollow" { Entry = followUser }
let TweetUserPoint = entryRequest "tweet" { Entry = tweetUser }
let re_tweetPoint = entryRequest "retweet" { Entry = re_tweet }

let ws = 
  choose [
    path "/livefeed" >=> handShake liveFeed
    Register_UserPoint
    LoginUserPoint
    FollowUserPoint
    TweetUserPoint
    re_tweetPoint
    pathScan "/search/%s"
      (fun searchkey ->
        let keyval = (sprintf "%s" searchkey)
        let reply = query keyval
        OK reply) 
    GET >=> choose [path "/" >=> Successful.OK "Welcome to tweeter!"]
  ]
[<EntryPoint>]
let main _=
  startWebServer defaultConfig ws
  0