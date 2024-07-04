import "./App.css";
import Header from "./component/layout/Header/Header.js";
import { BrowserRouter as Router, Routes, Route,Navigate } from "react-router-dom";
import Footer from "./component/layout/Footer/Footer.js";
import React from "react";
import Home from "./component/Home/Home.js";
import ProductDetails from "./component/Product/ProductDetails.js";
import Products from "./component/Product/Products.js";
import Search from "./component/Product/Search";
import LoginSignUp from "./component/User/LoginSignUp";

import store from "./store";
import { loadUser } from "./actions/userAction";

import UserOptions from "./component/layout/Header/UserOptions.js";
import { useSelector } from "react-redux";

import Profile from "./component/User/Profile.js";

import ProtectedRoute from "./component/Route/ProtectedRoute";

import UpdateProfile from "./component/User/UpdateProfile";
import UpdatePassword from "./component/User/UpdatePassword";
import ForgotPassword from "./component/User/ForgotPassword";
import ResetPassword from "./component/User/ResetPassword";
import Cart from "./component/Cart/Cart";
import Shipping from "./component/Cart/Shipping";
import ConfirmOrder from "./component/Cart/ConfirmOrder";
// import axios from "axios";
import Payment from "./component/Cart/Payment";
// import { Elements } from "@stripe/react-stripe-js";
//import { loadStripe } from "@stripe/stripe-js";
import OrderSuccess from "./component/Cart/OrderSuccess";
import MyOrders from "./component/Order/MyOrders";
import OrderDetails from "./component/Order/OrderDetails";
import Dashboard from "./component/Admin/Dashboard.js";
import ProductList from "./component/Admin/ProductList.js";
import NewProduct from "./component/Admin/NewProduct";
import UpdateProduct from "./component/Admin/UpdateProduct";
import OrderList from "./component/Admin/OrderList";
import ProcessOrder from "./component/Admin/ProcessOrder";
import UsersList from "./component/Admin/UsersList";
import UpdateUser from "./component/Admin/UpdateUser";
import ProductReviews from "./component/Admin/ProductReviews";
import Contact from "./component/layout/Contact/Contact";
import About from "./component/layout/About/About";
import NotFound from "./component/layout/Not Found/NotFound";

function App() {
    const { isAuthenticated, user } = useSelector((state) => state.user);

    //   const [stripeApiKey, setStripeApiKey] = useState("");

    //   async function getStripeApiKey() {
    //     const { data } = await axios.get("/api/v1/stripeapikey");

    //     setStripeApiKey(data.stripeApiKey);
    //   }

    React.useEffect(() => {
        store.dispatch(loadUser());
        // getStripeApiKey();
    }, []);

    //window.addEventListener("contextmenu", (e) => e.preventDefault());

    return (
        <Router>
            <Header />

            {isAuthenticated && <UserOptions user={user} />}

            {/* {stripeApiKey && (
                <Elements stripe={loadStripe(stripeApiKey)}>
                    <ProtectedRoute
                        exact
                        path="/process/payment"
                        component={Payment}
                    />
                </Elements>
            )} */}

            <Routes>
                <Route exact path="/" element={<Home />} />
                <Route exact path="/product/:id" element={<ProductDetails />} />
                <Route exact path="/products" element={<Products />} />

                <Route path="/products/:keyword" element={<Products />} />

                <Route exact path="/search" element={<Search />} />

                <Route element={<ProtectedRoute />}>
                    <Route exact path="/account" element={<Profile />} />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        exact
                        path="/me/update"
                        element={<UpdateProfile />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        exact
                        path="/password/update"
                        element={<UpdatePassword />}
                    />
                </Route>

                <Route
                    exact
                    path="/password/forgot"
                    element={<ForgotPassword />}
                />
                <Route
                    exact
                    path="/password/reset/:token"
                    element={<ResetPassword />}
                />

                <Route exact path="/cart" element={<Cart />} />

                <Route exact path="/login" element={<LoginSignUp />} />

                <Route element={<ProtectedRoute />}>
                    <Route
                        exact
                        path="/login/shipping"
                        element={<Shipping />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        exact
                        path="/order/confirm"
                        element={<ConfirmOrder />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        exact
                        path="/process/payment"
                        element={<Payment />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route exact path="/success" element={<OrderSuccess />} />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route exact path="/orders" element={<MyOrders />} />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route exact path="/order/:id" element={<OrderDetails />} />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/dashboard"
                        element={<Dashboard />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/products"
                        element={<ProductList />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/product"
                        element={<NewProduct />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/product/:id"
                        element={<UpdateProduct />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/orders"
                        element={<OrderList />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/order/:id"
                        element={<ProcessOrder />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/users"
                        element={<UsersList />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/user/:id"
                        element={<UpdateUser />}
                    />
                </Route>

                <Route element={<ProtectedRoute />}>
                    <Route
                        isAdmin={true}
                        exact
                        path="/admin/reviews"
                        element={<ProductReviews />}
                    />
                </Route>

                <Route exact path="/contact" element={<Contact />} />

                <Route exact path="/about" element={<About />} />

                <Route
                    path="*"
                    element={
                        window.location.pathname === "/process/payment" ? (
                            <Navigate to="/process/payment" />
                        ) : (
                            <NotFound />
                        )
                    }
                />
            </Routes>
            <Footer />
        </Router>
    );
}

export default App;
