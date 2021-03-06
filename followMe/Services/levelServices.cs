﻿using followMe.Models;
using Microsoft.AspNet.SignalR;
using System.Linq;
using MongoDB.Driver.Builders;
using MongoDB.Driver;

namespace followMe.Services
{
    public class levelServices : Hub
    {
        userMethods user = new userMethods();
        authServices auth = new authServices();
        communityServices comm = new communityServices();
        deployment deploy = new deployment();

        public levelList redirectToWorld(int worldName, string levelName, string username)
        {
            var db = deploy.getDB();
            var levels = db.GetCollection<levelList>("levelList");
            if (levelName == "" || levelName == null)//Come from JS
            {
                userDefined userToQuery = db.GetCollection<userDefined>("userDefined").FindOne(Query.EQ("username", username));
                var world = levels.FindOne(Query.And(
                    Query.EQ("worldNumber", userToQuery.world),
                    Query.EQ("identifier", userToQuery.level)
                    ));
                return world;
            }
            else
            {
                var world = levels.FindOne(Query.And(
                    Query.EQ("worldNumber", worldName),
                    Query.EQ("identifier", levelName)
                    ));
                return world;
            }
        }

        public string getImages(string level, string username, string helpUsername)
        {
            bool usingHelp = false;
            string usernameForClient = username;
            if(helpUsername != null)
            {
                username = helpUsername;
                usingHelp = true;
            }
            var username2 = user.changeStringDots(username, false);
            level = level + "ImagesDefinition";
            deployment deploy = new deployment();
            var server = deploy.getMongoClient();
            var mongo = server.GetServer();
            var db = mongo.GetDatabase("followme");

            if (usingHelp == false)
            {
                user.updateAccessTime("newAccess", username2);
            }
            if (level != "ImagesDefinition")
            {
                var collection = db.GetCollection<image>(level);

                var person = db.GetCollection<userDefined>("userDefined");
                var theUser = person.FindOne(Query.EQ("username", username));

                long howManySurfaces = collection.Find(Query.EQ("type", "surface")).Count();//To prevent duplicates from jQuery Mobile

                foreach (var item in collection.FindAll())//;//test for server
                {
                    var canAccessTeleport = false;
                    canAccessTeleport = auth.hasAccessToLevel(username2, item.level, item.world);
                    int totalLevelCount = 0;
                    int totalPlayerDoneCount = 0;
                    if (item.type == "teleports")
                    {
                        totalLevelCount = comm.targetProgressCount(username2, true, item.level, item.world, "all");
                        totalPlayerDoneCount = comm.targetProgressCount(username2, false, item.level, item.world, "all");
                    }

                    if ((theUser.difficulty >= item.showMinimumDifficulty || item.showMinimumDifficulty == 0 )&& ( theUser.difficulty <= item.hideMinimumDifficulty || item.hideMinimumDifficulty == 0))
                    {
                        Clients.All.addImageFromServer(item, item.type, usernameForClient, canAccessTeleport, howManySurfaces, totalLevelCount, totalPlayerDoneCount);//item.images.Find(x => x.animate));
                    }

                }
                

                if (theUser.checkpoint == -1)
                {
                    var startpoint = collection.FindOne(Query.EQ("startpoint", true));

                    Clients.All.Startpoint(
                        startpoint,
                        startpoint.y,
                        theUser,
                        true,
                        usernameForClient);
                    //Clients.All.setLocalCheckpoint(theUser.checkpoint);
                }
                if (theUser.checkpoint != -1)
                {
                    var startpoint = collection.Find(Query.Exists("checkpoint")).Where(
                        m => m.checkpoint == theUser.checkpoint).FirstOrDefault();

                    if(startpoint == null)
                    {
                        startpoint = collection.Find(Query.Exists("checkpoint")).Where(
                        m => m.checkpoint == 0).FirstOrDefault();
                    }
                    Clients.All.Startpoint(
                        startpoint,
                        startpoint.y + startpoint.heightY,
                        theUser,
                        true,
                        usernameForClient);
                    //Clients.All.setLocalCheckpoint(theUser.checkpoint, true);
                }
            }
            return "Complete";
        }
        public void updateCheckpoint(string username, int index, string levelname, int oldCheckpoint, string levelUnlocked, float timeToFinish, bool wasInvincible)
        {
            var username2 = user.changeStringDots(username, false);
            deployment deploy = new deployment();
            var server = deploy.getMongoClient();
            var mongo = server.GetServer();
            var db = mongo.GetDatabase("followme");

            var queryForLevel = levelname + "ImagesDefinition";
            var level = db.GetCollection<image>(queryForLevel);
            var collection = db.GetCollection<userDefined>("userDefined");
            
            var userToChange = collection.FindOne(Query.EQ("username", username2));
            
            if(wasInvincible)//They've survived, awarded client side after this call
            {
                userToChange.hasSurvived = wasInvincible;
            }

            userToChange.levelPlayTime = timeToFinish;
            collection.Save(userToChange);

            if (index == -1)//this is not really a checkpoint, new level
            {
                var levels = db.GetCollection<levelList>("levelList");
                var nextLevel = levels.FindOne(Query.And(
                Query.EQ("identifier", levelUnlocked)
                ));

                
                userToChange.checkpoint = 0;
                userToChange.level = nextLevel.identifier;
                userToChange.world = nextLevel.worldNumber;
                userToChange.levelPlayTime = 0;
                collection.Save(userToChange);

                auth.newLevelAccess(username2, levelUnlocked, 1);
                comm.addPlayerProgress(username2, nextLevel.fullName, nextLevel.worldName);
                comm.setFastestLevelTime(username2, levelname, timeToFinish);

                Clients.All.newLevel(nextLevel.fullName, nextLevel.worldName, username, false);
             
            }

            else
            {
                var clearAnimation = level.Find(Query.Exists("checkpoint")).Where(
                        m => m.checkpoint == userToChange.checkpoint).FirstOrDefault();

                userToChange.checkpoint = index;
                collection.Save(userToChange);
                var startpoint = level.Find(Query.Exists("checkpoint")).Where(
                        m => m.checkpoint == index).FirstOrDefault();

             
            }
        }
        public void redirectFromTeleport(string username, int worldNumber, string levelNumber, userDefined player)
        {
            deployment deploy = new deployment();
            var db = deploy.getDB();           
            var levels = db.GetCollection<levelList>("levelList");
            var users = db.GetCollection<userDefined>("userDefined");

            var userToUpdate = users.FindOneAs<userDefined>(Query.EQ("username", user.changeStringDots(username, true)));
            userToUpdate.levelPlayTime = 0;
            users.Save(userToUpdate);

            var world = levels.FindOne(Query.And(
                Query.EQ("worldNumber", worldNumber),
                Query.EQ("identifier", levelNumber)
                ));
            

            comm.addPlayerProgress(user.changeStringDots(username, true), world.fullName, world.worldName);
            Clients.All.newLevel(world.fullName, world.worldName, username, true);
        }
    }
}