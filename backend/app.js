const express = require("express");
const app = express();
const cookieParser = require("cookie-parser");

const bodyParser = require("body-parser");
const fileUpload = require("express-fileupload");
const path = require("path");

const errorMiddleware = require("./middleware/error");


//Config
if (process.env.NODE_ENV !== "PRODUCTION") {
  require("dotenv").config({ path: "backend/config/config.env" });
}


app.use(express.json());

app.use(cookieParser());

app.use(bodyParser.json({ limit: '50mb' }));
app.use(bodyParser.urlencoded({ limit: '50mb', extended: true }));
app.use(fileUpload());

app.get("/api/v1", function (req, res) {
    res.send("Hello World");
});

// routs import
const product = require("./routes/productRoute");
const user = require("./routes/userRout");
const order = require("./routes/orderRoute");
//const payment = require("./routes/paymentRoute.js");



app.use("/api/v1", product);
app.use("/api/v1", user);
app.use("/api/v1", order);
//app.use("/api/v1", payment);

app.use(express.static(path.join(__dirname, "../frontend/build")));

app.get("*", (req, res) => {
  res.sendFile(path.resolve(__dirname, "../frontend/build/index.html"));
});


//Middleware for error
app.use(errorMiddleware);

module.exports = app;
