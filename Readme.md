## Distributed operating system principles : Twitter-Clone (Part 2)
## Group Members 
- Ojasvi, Fnu (UFID: 8747-2335)
- Goel, Saksham (UFID: 9279-2975)
## How to Run the code:
Server will run first on port 8080 which will feed websockets to the http client where clients will run on localhost:8080. 
### Commands for server and client-
### Server
-  dotnet run 
### Client
To register a user:
- http://localhost:8080/newregisteration

To login a user:
- http://localhost:8080/login

To follow a user:
- http://localhost:8080/Userfollow

User tweets:
- http://localhost:8080/tweet

User Retweets:
- http://localhost:8080/retweet

tweet Search:
- http://localhost:8080/search/`Anytagname`

## User Testing and Implementation:
### 1) JSON-based API that represents all messages and their replies:

    -Client's Request- 
                    { 
                        "userName" : "UserName"
                        "value" :  " "
                    }

            Entry In the Value:-> 
            - For register a user : "Password"
            - For login a user : "Password"
            - For follow a user: "UserName"
            - For tweet a user: "what you want to tweet !!"
            - For retweet a user: "what you want to retweet 
            !!"

    -Server Response- 
        "userName": "user<userID>"
        "message": "Congrates!! User user6 started following you!"
        "service": "register/login/follow/tweet/reteet"
        "code": "OK/FAIL "

### 2) Implementation of the web socket interface:
- Hashmap is used to store the data and websockets are used to develop services in which data from the server has to be dynamically reflected on the client in real time.
- HTTP and REST are used for following functions for the user: register, login, follow, tweet, retweet and query. 
<div style="page-break-after: always;"></div>

### 3) Clients to use web sockets:
- Logging/Signing up: If the user is logging into twitter then websockets will be used to load the user's every activity into the UI. If user is signing for the first time, it will not have any feeds or activity and corresponding message will get appear in the user interface.
- Following a user: The server will notify both users(followed one and following one) for their actions using websockets.
- When user will sends a new tweet then request is sent to the server, and the same message is sent via websockets in the feed portion of the active follower's feed.

## Youtube Link:
-  https://youtu.be/Ahkzcqf5DDM
