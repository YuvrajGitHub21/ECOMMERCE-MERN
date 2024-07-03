import React, { useState, useEffect } from "react";
import "./ResetPassword.css";
import Loader from "../layout/Loader/Loader";
import { useDispatch, useSelector } from "react-redux";
import { clearErrors, resetPassword } from "../../actions/userAction";
import { useNavigate, useParams } from "react-router-dom";
import { useAlert } from "react-alert";
import MetaData from "../layout/MetaData";
import LockOpenIcon from "@mui/icons-material/LockOpen";
import LockIcon from "@mui/icons-material/Lock";

const ResetPassword = () => {
  const { token } = useParams();
  const dispatch = useDispatch();
  const navigate = useNavigate();
  const alert = useAlert();

  const { error, success, loading } = useSelector((state) => state.forgotPassword);

  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");

  const resetPasswordSubmit = (e) => {
    e.preventDefault();

    if (password !== confirmPassword) {
      alert.error("Passwords do not match");
      return;
    }

    dispatch(resetPassword(token, { password, confirmPassword }));
  };

  useEffect(() => {
    if (error) {
      alert.error(error);
      dispatch(clearErrors());
    }

    if (success) {
      alert.success("Password Updated Successfully");
      navigate("/login");
    }
  }, [dispatch, error, alert, success, navigate]);

  return (
    <div className="resetPasswordContainer">
      <MetaData title="Reset Password" />
      <div className="resetPasswordBox">
        <h2 className="resetPasswordHeading">Reset Password</h2>

        {loading ? (
          <Loader />
        ) : (
          <form className="resetPasswordForm" onSubmit={resetPasswordSubmit}>
            <div className="resetPasswordInput">
              <LockOpenIcon />
              <input
                type="password"
                placeholder="New Password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>
            <div className="resetPasswordInput">
              <LockIcon />
              <input
                type="password"
                placeholder="Confirm Password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                required
              />
            </div>
            <button type="submit" className="resetPasswordBtn">
              Update
            </button>
          </form>
        )}
      </div>
    </div>
  );
};

export default ResetPassword;
